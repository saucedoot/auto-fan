namespace AutoFan.Core;

/// <summary>
/// Holds a few whole-fan settings on the mixed heat and stops as a draft.
/// </summary>
public sealed class JointRunner
{
    private readonly IHardwareBackend _hardware;
    private readonly IWorkloadActuator _workload;
    private readonly ICompetingSoftwareScanner _scanner;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ThermalAbortLimits _limits;
    private readonly HeatProfile? _heat;
    private readonly IReadOnlyList<FanActuation> _actuation;
    private PumpWatch? _pumps;

    public JointRunner(
        IHardwareBackend hardware,
        IWorkloadActuator workload,
        ICompetingSoftwareScanner scanner,
        TimeProvider? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ThermalAbortLimits? limits = null,
        HeatProfile? heat = null,
        IReadOnlyList<FanActuation>? actuation = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _workload = workload ?? throw new ArgumentNullException(nameof(workload));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _clock = clock ?? TimeProvider.System;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _limits = ThermalAbortLimits.FromUser(limits?.CpuCelsius, limits?.GpuCelsius);
        _heat = heat;
        _actuation = actuation ?? [];
    }

    public async Task<JointSearchResult> RunAsync(
        FanTestRun screen,
        CancellationToken cancellationToken = default,
        IProgress<FanTestProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var attempts = new List<JointAttempt>();
        try
        {
            if (_heat is not HeatProfile heat)
            {
                return Stop(HeatProfile.MissingLampDetail, attempts);
            }

            _workload.ApplyLow(heat);
            (string? abort, HardwareSnapshot? reference) = await HoldAsync(
                commands: [],
                progress,
                "Holding fans on BIOS before trying combinations.",
                cancellationToken).ConfigureAwait(false);
            if (abort is not null || reference is null)
            {
                return Stop(abort ?? "The BIOS reference did not settle.", attempts);
            }

            var maxRpm = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (FanActuation fan in _actuation)
            {
                if (fan.MaximumRpm is > 0)
                {
                    maxRpm[fan.FanGroupId] = fan.MaximumRpm.Value;
                }
            }

            List<JointVector> vectors = JointPlan.Select(reference, screen.Influence, _actuation, _limits).ToList();
            bool pairUsed = false;
            for (int index = 0; index < vectors.Count && attempts.Count < JointPlan.MaxVectors; index++)
            {
                JointVector vector = vectors[index];
                progress?.Report(new FanTestProgress(
                    string.Empty,
                    string.Empty,
                    index + 1,
                    vectors.Count,
                    FanTestStage.Screen,
                    reference,
                    $"Trying fan combination {index + 1}. Nothing is applied yet."));
                (string? holdAbort, HardwareSnapshot? held) = await HoldAsync(
                    vector.Commands,
                    progress,
                    "Holding this fan combination.",
                    cancellationToken).ConfigureAwait(false);
                if (holdAbort is not null)
                {
                    return Stop(holdAbort, attempts);
                }

                if (held is null)
                {
                    continue;
                }

                JointAttempt attempt = Judge(vector, reference, held, maxRpm);
                attempts.Add(attempt);
                bool residual = Residual(vector, held);
                if (JointPlan.OnePair(vector.Commands, screen.Influence, residual, pairUsed) is IReadOnlyList<DutyCommand> pair)
                {
                    pairUsed = true;
                    vectors.Add(new JointVector(pair, PredictedCpuCelsius: null, PredictedGpuCelsius: null));
                }
            }

            JointAttempt? chosen = Choose(attempts);
            string detail = chosen is null
                ? "No fan combination beat the BIOS reference. Draft only. Nothing is applied."
                : chosen.LikelyQuieter
                    ? "One draft combination looks quieter than BIOS and was not hotter. Nothing is applied."
                    : "One draft combination held and was not hotter than BIOS. Nothing is applied.";
            return new JointSearchResult(AbortDetail: null, chosen, attempts, detail);
        }
        catch (OperationCanceledException)
        {
            return Stop("Combination search cancelled.", attempts);
        }
        finally
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
        }
    }

    private JointAttempt Judge(
        JointVector vector,
        HardwareSnapshot reference,
        HardwareSnapshot held,
        IReadOnlyDictionary<string, double> maxRpm)
    {
        string? leftover = Hotter(reference, held);
        bool accepted = _lastAssessment == HoldAssessment.SettledMeasured && leftover is null;
        FanEffort.Score score = FanEffort.Of(held, maxRpm);
        bool quieter = accepted && FanEffort.LikelyQuieter(reference, held, maxRpm);
        return new JointAttempt(
            vector.Commands,
            _lastAssessment,
            accepted,
            quieter,
            score.Highest,
            score.Total,
            accepted ? null : leftover ?? _lastAssessment.ToString());
    }

    private HoldAssessment _lastAssessment = HoldAssessment.TransientModeled;

    private static JointAttempt? Choose(IReadOnlyList<JointAttempt> attempts)
    {
        return attempts
            .Where(attempt => attempt.Accepted)
            .OrderBy(attempt => attempt.HighestEffort ?? double.MaxValue)
            .ThenBy(attempt => attempt.TotalEffort ?? double.MaxValue)
            .ThenBy(attempt => attempt.Commands.Sum(command => command.DutyPercent))
            .FirstOrDefault();
    }

    private static bool Residual(JointVector vector, HardwareSnapshot held)
    {
        if (vector.PredictedCpuCelsius is not double predicted)
        {
            return false;
        }

        double? actual = PreferredTemperature.Read(held, SensorKind.CpuTemperature);
        return actual is double value
            && Math.Abs(value - predicted) > HoldAssessor.MaxWindowRangeCelsius;
    }

    private static string? Hotter(HardwareSnapshot reference, HardwareSnapshot hold)
    {
        if (Rose(reference, hold, SensorKind.CpuTemperature)
            || Rose(reference, hold, SensorKind.GpuTemperature)
            || Rose(reference, hold, SensorKind.VrmTemperature)
            || Rose(reference, hold, SensorKind.CaseTemperature)
            || Rose(reference, hold, SensorKind.MotherboardTemperature))
        {
            return "This combination ran hotter than the BIOS reference.";
        }

        if (Missing(reference, hold, SensorKind.CpuTemperature))
        {
            return "A needed temperature was missing, so this combination is not a pass.";
        }

        return null;
    }

    private static bool Missing(HardwareSnapshot reference, HardwareSnapshot hold, SensorKind kind) =>
        PreferredTemperature.Read(reference, kind) is not null
        && PreferredTemperature.Read(hold, kind) is null;

    private static bool Rose(HardwareSnapshot reference, HardwareSnapshot hold, SensorKind kind)
    {
        double? before = PreferredTemperature.Read(reference, kind);
        double? after = PreferredTemperature.Read(hold, kind);
        return before is double from
            && after is double to
            && to > from + HoldAssessor.MaxWindowRangeCelsius;
    }

    private async Task<(string? Abort, HardwareSnapshot? Held)> HoldAsync(
        IReadOnlyList<DutyCommand> commands,
        IProgress<FanTestProgress>? progress,
        string message,
        CancellationToken cancellationToken)
    {
        using var session = new SafeFanSession(_hardware, _scanner, _clock, limits: _limits);
        foreach (DutyCommand command in commands)
        {
            DutySetResult write = session.TrySetDuty(command.GroupId, command.DutyPercent);
            if (session.IsAborted || !write.Accepted)
            {
                return (session.AbortDetail ?? write.Error ?? "A fan command was rejected.", null);
            }
        }

        var window = new List<HardwareSnapshot>();
        DateTimeOffset deadline = _clock.GetUtcNow() + FanTestSchedule.Default.Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = _clock.GetUtcNow();
            HardwareSnapshot snapshot = _hardware.ReadSnapshot() with { CapturedAt = now };
            _pumps ??= PumpWatch.Capture(snapshot);
            if (_pumps.Check(snapshot) is string pumpSilent)
            {
                return (pumpSilent, null);
            }

            if (PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature) is null)
            {
                return ("CPU temperature disappeared. Fans were not turned down.", null);
            }

            ThermalAbortReason? ceiling = SafetyLimits.EvaluateWhileHeating(snapshot, _workload, _limits);
            if (ceiling is not null)
            {
                return (SafetyLimits.Describe(ceiling.Value, _limits), null);
            }

            if (commands.Count > 0 && commands.Any(command => Rpm(snapshot, command.GroupId) is not > 0))
            {
                _lastAssessment = HoldAssessment.FanStalled;
                return (null, snapshot);
            }

            window.Add(snapshot);
            progress?.Report(new FanTestProgress(string.Empty, string.Empty, 0, 0, FanTestStage.Screen, snapshot, message));
            bool timedOut = now >= deadline;
            HoldAssessment assessment = HoldAssessor.Evaluate(window, timedOut);
            if (assessment != HoldAssessment.TransientModeled)
            {
                _lastAssessment = assessment;
                return (null, snapshot);
            }

            await _delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private static double? Rpm(HardwareSnapshot snapshot, string id)
    {
        foreach (FanGroup group in snapshot.FanGroups)
        {
            if (string.Equals(group.Id, id, StringComparison.Ordinal))
            {
                return group.Rpm;
            }
        }

        return null;
    }

    private static JointSearchResult Stop(string detail, List<JointAttempt> attempts) =>
        new(detail, Chosen: null, attempts, detail);
}
