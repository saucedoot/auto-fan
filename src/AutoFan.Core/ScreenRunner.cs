namespace AutoFan.Core;

/// <summary>
/// Reference, step, reference for a few duties. Ranks stay Modeled.
/// This does not build a curve.
/// </summary>
public sealed class ScreenRunner
{
    public const string DidNotReturnDetail =
        "The reference did not repeat, so this step is not a fan effect.";

    private readonly IHardwareBackend _hardware;
    private readonly IWorkloadActuator _workload;
    private readonly ICompetingSoftwareScanner _scanner;
    private readonly IFanTestStore _store;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ThermalAbortLimits _limits;
    private readonly HeatProfile? _lowHeat;
    private readonly IReadOnlyList<FanActuation> _actuation;
    private readonly List<FanTestSample> _samples = [];
    private readonly List<InfluenceEntry> _influence = [];
    private readonly List<SkippedFanGroup> _skipped = [];
    private PumpWatch? _pumps;
    private int _nextHoldId;
    private HoldAssessment _lastAssessment;
    private List<HardwareSnapshot> _lastWindow = [];

    public ScreenRunner(
        IHardwareBackend hardware,
        IWorkloadActuator workload,
        ICompetingSoftwareScanner scanner,
        IFanTestStore store,
        TimeProvider? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ThermalAbortLimits? limits = null,
        HeatProfile? lowHeat = null,
        IReadOnlyList<FanActuation>? actuation = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _workload = workload ?? throw new ArgumentNullException(nameof(workload));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _limits = ThermalAbortLimits.FromUser(limits?.CpuCelsius, limits?.GpuCelsius);
        _lowHeat = lowHeat;
        _actuation = actuation ?? [];
    }

    public async Task<FanTestRun> RunAsync(
        CancellationToken cancellationToken = default,
        IProgress<FanTestProgress>? progress = null,
        IReadOnlyList<string>? onlyGroupIds = null)
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset started = _clock.GetUtcNow();
        _samples.Clear();
        _influence.Clear();
        _skipped.Clear();
        _pumps = null;

        try
        {
            if (_lowHeat is not HeatProfile heat)
            {
                return Finish(id, started, FanTestRunStatus.Aborted, HeatProfile.MissingLampDetail);
            }

            string? cool = await WaitUntilCoolAsync(progress, cancellationToken).ConfigureAwait(false);
            if (cool is not null)
            {
                return Finish(id, started, FanTestRunStatus.Aborted, cool);
            }

            _workload.ApplyLow(heat);
            string? abort = await ScreenGroupsAsync(progress, onlyGroupIds, cancellationToken).ConfigureAwait(false);
            FanTestRunStatus status = abort is null ? FanTestRunStatus.Completed : FanTestRunStatus.Aborted;
            return Finish(id, started, status, abort);
        }
        catch (OperationCanceledException)
        {
            return Finish(id, started, FanTestRunStatus.Cancelled, "Screen cancelled.");
        }
        finally
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
        }
    }

    private async Task<string?> ScreenGroupsAsync(
        IProgress<FanTestProgress>? progress,
        IReadOnlyList<string>? onlyGroupIds,
        CancellationToken cancellationToken)
    {
        var candidates = new List<FanGroup>();
        foreach (FanGroup group in _hardware.FanGroups)
        {
            if (group.Kind == FanGroupKind.Pump)
            {
                _skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.Pump));
                continue;
            }

            if (!FanWriteCandidates.IsWritableTestFan(group) || !FanWriteCandidates.HasTachometer(group))
            {
                continue;
            }

            if (onlyGroupIds is { Count: > 0 }
                && !onlyGroupIds.Contains(group.Id, StringComparer.Ordinal))
            {
                continue;
            }

            candidates.Add(group);
        }

        foreach (FanGroup group in FanWriteCandidates.TakeOnePerCoupledSet(candidates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? abort = await ScreenGroupAsync(group, progress, cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return abort;
            }
        }

        return null;
    }

    private async Task<string?> ScreenGroupAsync(
        FanGroup group,
        IProgress<FanTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        int referenceDuty = group.DutyCyclePercent is int found && found > 0 ? found : 40;
        FanActuation? actuation = _actuation.FirstOrDefault(item =>
            string.Equals(item.FanGroupId, group.Id, StringComparison.Ordinal));
        using var session = new SafeFanSession(_hardware, _scanner, _clock, limits: _limits);
        foreach (int testDuty in ScreenPlan.TestDuties(actuation))
        {
            if (testDuty == referenceDuty)
            {
                continue;
            }

            progress?.Report(new FanTestProgress(
                group.Id,
                group.Name,
                1,
                1,
                FanTestStage.Reference,
                _hardware.ReadSnapshot(),
                $"Holding {group.Name} at {referenceDuty}% before trying {testDuty}%."));
            string? abort = await HoldAsync(
                session,
                group,
                referenceDuty,
                FanTestStage.Reference,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return abort;
            }

            HoldAssessment beforeAssessment = _lastAssessment;
            List<HardwareSnapshot> before = _lastWindow;
            abort = await TransientAsync(session, group, testDuty, cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return abort;
            }

            HardwareSnapshot step = _lastWindow[^1];
            abort = await HoldAsync(
                session,
                group,
                referenceDuty,
                FanTestStage.Reference,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return abort;
            }

            bool returned = beforeAssessment == HoldAssessment.SettledMeasured
                && _lastAssessment == HoldAssessment.SettledMeasured
                && Returned(before, _lastWindow);
            RecordRank(group, referenceDuty, testDuty, before, step, returned);
        }

        return null;
    }

    private void RecordRank(
        FanGroup group,
        int referenceDuty,
        int testDuty,
        IReadOnlyList<HardwareSnapshot> before,
        HardwareSnapshot step,
        bool returned)
    {
        foreach (InfluenceTarget target in new[] { InfluenceTarget.Cpu, InfluenceTarget.Gpu })
        {
            double? from = Mean(before, target);
            double? to = PreferredTemperature.Read(step, target);
            bool ranked = returned && from is not null && to is not null;
            _influence.Add(new InfluenceEntry(
                group.Id,
                group.Name,
                target,
                ranked ? to - from : null,
                Effect: null,
                ranked ? MetricEvidence.Modeled : MetricEvidence.Unknown,
                referenceDuty,
                testDuty,
                Rpm(before.Count == 0 ? null : before[^1], group.Id),
                Rpm(step, group.Id),
                ranked ? null : DidNotReturnDetail));
        }
    }

    private async Task<string?> HoldAsync(
        SafeFanSession session,
        FanGroup group,
        int duty,
        FanTestStage stage,
        CancellationToken cancellationToken)
    {
        DutySetResult write = session.TrySetDuty(group.Id, duty);
        if (session.IsAborted)
        {
            return session.AbortDetail ?? write.Error;
        }

        if (!write.Accepted)
        {
            _skipped.Add(new SkippedFanGroup(group.Id, group.Name, write.Error ?? FanTestReasons.NotControllable));
            _lastAssessment = HoldAssessment.Aborted;
            _lastWindow = [];
            return null;
        }

        var window = new List<HardwareSnapshot>();
        int holdId = _nextHoldId++;
        DateTimeOffset deadline = _clock.GetUtcNow() + FanTestSchedule.Default.Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? stop = Read(group, stage, duty, holdId, window);
            if (stop is not null)
            {
                return stop;
            }

            bool timedOut = _clock.GetUtcNow() >= deadline;
            HoldAssessment assessment = HoldAssessor.Evaluate(window, timedOut, group.Id, dutyCommanded: true);
            if (assessment != HoldAssessment.TransientModeled)
            {
                _lastAssessment = assessment;
                _lastWindow = window;
                Stamp(holdId, assessment, assessment == HoldAssessment.SettledMeasured);
                return null;
            }

            await _delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> TransientAsync(
        SafeFanSession session,
        FanGroup group,
        int duty,
        CancellationToken cancellationToken)
    {
        DutySetResult write = session.TrySetDuty(group.Id, duty);
        if (session.IsAborted)
        {
            return session.AbortDetail ?? write.Error;
        }

        if (!write.Accepted)
        {
            return null;
        }

        var window = new List<HardwareSnapshot>();
        int holdId = _nextHoldId++;
        for (int sample = 0; sample < 5; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? stop = Read(group, FanTestStage.Perturb, duty, holdId, window);
            if (stop is not null)
            {
                return stop;
            }

            if (sample < 4)
            {
                await _delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        _lastWindow = window;
        Stamp(holdId, HoldAssessment.TransientModeled, settled: false);
        return null;
    }

    private string? Read(
        FanGroup group,
        FanTestStage stage,
        int duty,
        int holdId,
        List<HardwareSnapshot> window)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        HardwareSnapshot snapshot = _hardware.ReadSnapshot() with { CapturedAt = now };
        _pumps ??= PumpWatch.Capture(snapshot);
        if (_pumps.Check(snapshot) is string pumpSilent)
        {
            return pumpSilent;
        }

        if (PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature) is null)
        {
            return "CPU temperature disappeared. Fans were not turned down.";
        }

        ThermalAbortReason? ceiling = SafetyLimits.EvaluateWhileHeating(snapshot, _workload, _limits);
        if (ceiling is not null)
        {
            return SafetyLimits.Describe(ceiling.Value, _limits);
        }

        window.Add(snapshot);
        _samples.Add(new FanTestSample(
            now,
            group.Id,
            group.Name,
            stage,
            snapshot,
            Settled: false,
            HeatId: HeatId.Low,
            CommandedDutyPercent: duty,
            HoldId: holdId));
        return null;
    }

    private void Stamp(int holdId, HoldAssessment assessment, bool settled)
    {
        for (int index = 0; index < _samples.Count; index++)
        {
            if (_samples[index].HoldId != holdId)
            {
                continue;
            }

            _samples[index] = _samples[index] with
            {
                Assessment = assessment,
                Settled = settled,
            };
        }
    }

    private static bool Returned(IReadOnlyList<HardwareSnapshot> before, IReadOnlyList<HardwareSnapshot> after)
    {
        if (!SensorReturned(before, after, InfluenceTarget.Cpu))
        {
            return false;
        }

        if (after.Count > 0
            && PreferredTemperature.Read(after[^1], InfluenceTarget.Gpu) is not null)
        {
            return SensorReturned(before, after, InfluenceTarget.Gpu);
        }

        return true;
    }

    private static bool SensorReturned(
        IReadOnlyList<HardwareSnapshot> before,
        IReadOnlyList<HardwareSnapshot> after,
        InfluenceTarget target)
    {
        double? first = Mean(before, target);
        double? second = Mean(after, target);
        if (first is not double from || second is not double to)
        {
            return false;
        }

        return ScreenPlan.TemperatureReturned(from, Range(before, target), to);
    }

    private static double? Mean(IReadOnlyList<HardwareSnapshot> snapshots, InfluenceTarget target)
    {
        var values = new List<double>();
        foreach (HardwareSnapshot snapshot in snapshots)
        {
            if (PreferredTemperature.Read(snapshot, target) is double value)
            {
                values.Add(value);
            }
        }

        return values.Count == 0 ? null : values.Average();
    }

    private static double Range(IReadOnlyList<HardwareSnapshot> snapshots, InfluenceTarget target)
    {
        var values = new List<double>();
        foreach (HardwareSnapshot snapshot in snapshots)
        {
            if (PreferredTemperature.Read(snapshot, target) is double value)
            {
                values.Add(value);
            }
        }

        return values.Count == 0 ? 0 : values.Max() - values.Min();
    }

    private static double? Rpm(HardwareSnapshot? snapshot, string fanGroupId)
    {
        if (snapshot is null)
        {
            return null;
        }

        foreach (FanGroup group in snapshot.FanGroups)
        {
            if (string.Equals(group.Id, fanGroupId, StringComparison.Ordinal))
            {
                return group.Rpm;
            }
        }

        return null;
    }

    private async Task<string?> WaitUntilCoolAsync(
        IProgress<FanTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + ExperimentStartGate.Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HardwareSnapshot snapshot = _hardware.ReadSnapshot();
            if (ExperimentStartGate.IsReady(snapshot, out string detail))
            {
                return null;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return detail;
            }

            progress?.Report(new FanTestProgress(
                string.Empty,
                string.Empty,
                0,
                0,
                FanTestStage.Reference,
                snapshot,
                detail));
            await _delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private FanTestRun Finish(Guid id, DateTimeOffset started, FanTestRunStatus status, string? detail)
    {
        var run = new FanTestRun(
            id,
            started,
            _clock.GetUtcNow(),
            status,
            detail,
            _workload.GpuLoadAvailable,
            _samples.ToArray(),
            _influence.ToArray(),
            _skipped.ToArray());
        _store.Save(run);
        return run;
    }
}
