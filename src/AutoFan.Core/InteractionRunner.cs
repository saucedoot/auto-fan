namespace AutoFan.Core;

public sealed class InteractionRunner
{
    private readonly IHardwareBackend _hardware;
    private readonly IWorkloadActuator _workload;
    private readonly ICompetingSoftwareScanner _scanner;
    private readonly IInteractionStore _store;
    private readonly TimeProvider _clock;
    private readonly FanTestSchedule _schedule;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ThermalTrend _trend = new();
    private readonly ThermalAbortLimits _limits;
    private readonly FanPresence? _presence;

    public InteractionRunner(
        IHardwareBackend hardware,
        IWorkloadActuator workload,
        ICompetingSoftwareScanner scanner,
        IInteractionStore store,
        TimeProvider? clock = null,
        FanTestSchedule? schedule = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ThermalAbortLimits? limits = null,
        FanPresence? presence = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _workload = workload ?? throw new ArgumentNullException(nameof(workload));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
        _schedule = schedule ?? FanTestSchedule.Default;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _limits = ThermalAbortLimits.FromUser(limits?.CpuCelsius, limits?.GpuCelsius);
        _presence = presence is { Completed: true } ? presence : null;
    }

    public async Task<InteractionRun> RunAsync(
        CancellationToken cancellationToken = default,
        IProgress<InteractionProgress>? progress = null,
        IReadOnlyList<InfluenceEntry>? influence = null)
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset startedAt = _clock.GetUtcNow();
        var samples = new List<InteractionSample>();
        var skipped = new List<SkippedFanGroup>();

        try
        {
            CollectSkips(skipped);
            IReadOnlyList<(FanGroup First, FanGroup Second)> pairs = InteractionSelector.Select(
                _hardware.FanGroups,
                influence,
                _presence);
            string? waitAbort = await WaitToCoolAsync(startedAt, progress, cancellationToken).ConfigureAwait(false);
            if (waitAbort is not null)
            {
                return Finish(id, startedAt, samples, skipped, FanTestRunStatus.Aborted, waitAbort);
            }

            _workload.Set(WorkloadLevel.Low);
            for (int index = 0; index < pairs.Count; index++)
            {
                FanGroup first = FindGroup(pairs[index].First.Id) ?? pairs[index].First;
                FanGroup second = FindGroup(pairs[index].Second.Id) ?? pairs[index].Second;
                string? abort = await TestPairAsync(
                    first,
                    second,
                    index,
                    pairs.Count,
                    startedAt,
                    samples,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                if (abort is not null)
                {
                    return Finish(id, startedAt, samples, skipped, FanTestRunStatus.Aborted, abort);
                }
            }

            return Finish(id, startedAt, samples, skipped, FanTestRunStatus.Completed, abortDetail: null);
        }
        catch (OperationCanceledException)
        {
            return Finish(id, startedAt, samples, skipped, FanTestRunStatus.Cancelled, "Interaction tests cancelled.");
        }
        catch
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            throw;
        }
    }

    private void CollectSkips(List<SkippedFanGroup> skipped)
    {
        foreach (FanGroup group in _hardware.FanGroups)
        {
            if (group.Kind == FanGroupKind.Pump)
            {
                skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.Pump));
            }
            else if (FanWriteCandidates.IsGpuHeader(group) && !FanWriteCandidates.IsWritableNvidiaGpuFan(group))
            {
                skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.Gpu));
            }
            else if (!FanWriteCandidates.IsWritableTestFan(group) && !group.IsControllable)
            {
                skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.NotControllable));
            }
            else if (FanWriteCandidates.IsWritableTestFan(group)
                && !FanWriteCandidates.HasUsableTachometer(group, _presence))
            {
                skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.NoRpm));
            }
        }
    }

    private async Task<string?> WaitToCoolAsync(
        DateTimeOffset startedAt,
        IProgress<InteractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + ExperimentStartGate.Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DurationAbort(startedAt) is string duration)
            {
                return duration;
            }

            HardwareSnapshot snapshot = _hardware.ReadSnapshot();
            _trend.Add(_clock.GetUtcNow(), snapshot);
            ThermalAbortReason? abort = SafetyLimits.Evaluate(snapshot, _limits) ?? _trend.Evaluate(_limits);
            if (abort is not null)
            {
                return SafetyLimits.Describe(abort.Value, _limits);
            }

            if (ExperimentStartGate.IsReady(snapshot, out string detail))
            {
                progress?.Report(new InteractionProgress(snapshot, detail));
                return null;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return "CPU or GPU stayed above "
                    + $"{ExperimentStartGate.CpuReadyCelsius} °C for {ExperimentStartGate.Timeout.TotalMinutes:0} minutes. "
                    + "Wait to cool, then try again.";
            }

            progress?.Report(new InteractionProgress(snapshot, detail));
            await _delay(_schedule.SamplePeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> TestPairAsync(
        FanGroup first,
        FanGroup second,
        int index,
        int count,
        DateTimeOffset startedAt,
        List<InteractionSample> samples,
        IProgress<InteractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? firstAbort = await RunAloneAsync(
            first,
            second,
            InteractionStep.ReferenceFirst,
            InteractionStep.First,
            startedAt,
            samples,
            progress,
            index + 1,
            count,
            cancellationToken).ConfigureAwait(false);
        if (firstAbort is not null)
        {
            return firstAbort;
        }

        string? secondAbort = await RunAloneAsync(
            first,
            second,
            InteractionStep.ReferenceSecond,
            InteractionStep.Second,
            startedAt,
            samples,
            progress,
            index + 1,
            count,
            cancellationToken).ConfigureAwait(false);
        if (secondAbort is not null)
        {
            return secondAbort;
        }

        return await RunCombinedAsync(
            first,
            second,
            startedAt,
            samples,
            progress,
            index + 1,
            count,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> RunAloneAsync(
        FanGroup first,
        FanGroup second,
        InteractionStep reference,
        InteractionStep perturb,
        DateTimeOffset startedAt,
        List<InteractionSample> samples,
        IProgress<InteractionProgress>? progress,
        int pairNumber,
        int pairCount,
        CancellationToken cancellationToken)
    {
        FanGroup active = perturb == InteractionStep.First ? first : second;
        string? hold = await SampleAsync(
            first,
            second,
            reference,
            startedAt,
            samples,
            session: null,
            progress,
            HoldMessage(active.Name, pairNumber, pairCount),
            cancellationToken).ConfigureAwait(false);
        if (hold is not null)
        {
            return hold;
        }

        FanGroup live = FindGroup(active.Id) ?? active;
        if (!TryTargetDuty(live, out int duty, out string? skip))
        {
            return skip;
        }

        using var session = new SafeFanSession(_hardware, _scanner, _clock, limits: _limits);
        DutySetResult write = session.TrySetDuty(live.Id, duty);
        if (!write.Accepted)
        {
            return write.Error ?? session.AbortDetail ?? "Interaction test write was rejected.";
        }

        return await SampleAsync(
            first,
            second,
            perturb,
            startedAt,
            samples,
            session,
            progress,
            AloneMessage(active.Name, pairNumber, pairCount),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> RunCombinedAsync(
        FanGroup first,
        FanGroup second,
        DateTimeOffset startedAt,
        List<InteractionSample> samples,
        IProgress<InteractionProgress>? progress,
        int pairNumber,
        int pairCount,
        CancellationToken cancellationToken)
    {
        string? hold = await SampleAsync(
            first,
            second,
            InteractionStep.ReferenceCombined,
            startedAt,
            samples,
            session: null,
            progress,
            HoldBothMessage(first.Name, second.Name, pairNumber, pairCount),
            cancellationToken).ConfigureAwait(false);
        if (hold is not null)
        {
            return hold;
        }

        FanGroup liveFirst = FindGroup(first.Id) ?? first;
        FanGroup liveSecond = FindGroup(second.Id) ?? second;
        if (!TryTargetDuty(liveFirst, out int firstDuty, out string? firstSkip))
        {
            return firstSkip;
        }

        if (!TryTargetDuty(liveSecond, out int secondDuty, out string? secondSkip))
        {
            return secondSkip;
        }

        using var session = new SafeFanSession(_hardware, _scanner, _clock, limits: _limits);
        DutySetResult writeFirst = session.TrySetDuty(liveFirst.Id, firstDuty);
        if (!writeFirst.Accepted)
        {
            return writeFirst.Error ?? session.AbortDetail ?? "Interaction test write was rejected.";
        }

        DutySetResult writeSecond = session.TrySetDuty(liveSecond.Id, secondDuty);
        if (!writeSecond.Accepted)
        {
            return writeSecond.Error ?? session.AbortDetail ?? "Interaction test write was rejected.";
        }

        return await SampleAsync(
            first,
            second,
            InteractionStep.Combined,
            startedAt,
            samples,
            session,
            progress,
            CombinedMessage(first.Name, second.Name, pairNumber, pairCount),
            cancellationToken).ConfigureAwait(false);
    }

    private static string HoldMessage(string name, int pairNumber, int pairCount) =>
        $"Measuring normal BIOS speeds before changing {name}. Pair {pairNumber} of {pairCount}. Waiting until temperatures stop moving. Fans still on BIOS.";

    private static string AloneMessage(string name, int pairNumber, int pairCount) =>
        $"Speeding up {name} only. It goes back to BIOS after. Pair {pairNumber} of {pairCount}. Waiting until temperatures stop moving.";

    private static string HoldBothMessage(string first, string second, int pairNumber, int pairCount) =>
        $"Measuring normal BIOS speeds before changing {first} and {second} together. Pair {pairNumber} of {pairCount}. Waiting until temperatures stop moving. Fans still on BIOS.";

    private static string CombinedMessage(string first, string second, int pairNumber, int pairCount) =>
        $"Speeding up {first} and {second} together. Both go back to BIOS after. Pair {pairNumber} of {pairCount}. Waiting until temperatures stop moving.";

    private async Task<string?> SampleAsync(
        FanGroup first,
        FanGroup second,
        InteractionStep step,
        DateTimeOffset startedAt,
        List<InteractionSample> samples,
        SafeFanSession? session,
        IProgress<InteractionProgress>? progress,
        string message,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<HardwareSnapshot>();
        DateTimeOffset deadline = _clock.GetUtcNow() + _schedule.Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DurationAbort(startedAt) is string durationAbort)
            {
                return durationAbort;
            }

            session?.CheckLimits();
            if (session is { IsAborted: true })
            {
                return session.AbortDetail ?? "Interaction test aborted by safety limits.";
            }

            HardwareSnapshot snapshot = _hardware.ReadSnapshot();
            DateTimeOffset now = _clock.GetUtcNow();
            _trend.Add(now, snapshot);
            if (_trend.Evaluate(_limits) is ThermalAbortReason rise)
            {
                return SafetyLimits.Describe(rise);
            }

            samples.Add(new InteractionSample(
                now,
                first.Id,
                first.Name,
                second.Id,
                second.Name,
                step,
                snapshot));
            snapshots.Add(snapshot);
            progress?.Report(new InteractionProgress(snapshot, message));

            if (SafetyLimits.EvaluateWhileHeating(snapshot, _workload, _limits) is ThermalAbortReason abort)
            {
                return SafetyLimits.Describe(abort, _limits);
            }

            if (TemperatureSettle.RelevantTempsSettled(snapshots) || now >= deadline)
            {
                return null;
            }

            await _delay(_schedule.SamplePeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private InteractionRun Finish(
        Guid id,
        DateTimeOffset startedAt,
        IReadOnlyList<InteractionSample> samples,
        IReadOnlyList<SkippedFanGroup> skipped,
        FanTestRunStatus status,
        string? abortDetail)
    {
        _workload.Stop();
        _hardware.RestoreDefaults();
        var run = new InteractionRun(
            id,
            startedAt,
            _clock.GetUtcNow(),
            status,
            abortDetail,
            _workload.GpuLoadAvailable,
            samples.ToArray(),
            InteractionBuilder.Build(samples),
            skipped.ToArray());
        _store.Save(run);
        return run;
    }

    private FanGroup? FindGroup(string id) =>
        _hardware.FanGroups.FirstOrDefault(fan => fan.Id == id);

    private static bool TryTargetDuty(FanGroup group, out int duty, out string? error)
    {
        int current = group.DutyCyclePercent ?? 40;
        duty = Math.Min(SafetyLimits.MaxDutyPercent, current + FanTestSchedule.DutyStepPercent);
        if (group.DutyCyclePercent >= SafetyLimits.MaxDutyPercent || duty <= current)
        {
            error = $"{group.Name}: {FanTestReasons.AlreadyAtMaxDuty}";
            return false;
        }

        error = null;
        return true;
    }

    private string? DurationAbort(DateTimeOffset startedAt)
    {
        if (_clock.GetUtcNow() - startedAt >= TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes))
        {
            return $"Interaction tests reached the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.";
        }

        return null;
    }
}
