namespace AutoFan.Core;

/// <summary>
/// One scripted hold against a hardware surface. It does not build curves.
/// Pass, failure, and cancel all return the fans before the result is visible.
/// </summary>
public sealed class OptimizationSession
{
    private readonly IHardwareBackend _hardware;
    private readonly ICompetingSoftwareScanner _scanner;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _samplePeriod;
    private readonly TimeSpan _timeout;

    public OptimizationSession(
        IHardwareBackend hardware,
        TimeProvider clock,
        Func<TimeSpan, CancellationToken, Task> delay,
        ICompetingSoftwareScanner? scanner = null,
        TimeSpan? samplePeriod = null,
        TimeSpan? timeout = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _scanner = scanner ?? EmptyCompetingSoftwareScanner.Instance;
        _samplePeriod = samplePeriod ?? TimeSpan.FromSeconds(1);
        _timeout = timeout ?? TimeSpan.FromSeconds(90);
    }

    public async Task<OptimizationSessionResult> RunAsync(
        string fanGroupId,
        int dutyPercent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);
        var duties = new List<int>();
        OptimizationSessionResult result;
        using (var session = new SafeFanSession(_hardware, _scanner, _clock))
        {
            try
            {
                result = await RunCoreAsync(session, fanGroupId, dutyPercent, duties, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = Finish(OptimizationSessionStatus.Cancelled, assessment: null, duties, "Cancelled.");
            }
        }

        return result with { SoftwareControlReleased = !_hardware.HasActiveSoftwareControl };
    }

    private async Task<OptimizationSessionResult> RunCoreAsync(
        SafeFanSession session,
        string fanGroupId,
        int dutyPercent,
        List<int> duties,
        CancellationToken cancellationToken)
    {
        DutySetResult write = session.TrySetDuty(fanGroupId, dutyPercent);
        if (!write.Accepted)
        {
            return Finish(OptimizationSessionStatus.Failed, assessment: null, duties, write.Error);
        }

        duties.Add(dutyPercent);
        var snapshots = new List<HardwareSnapshot>();
        DateTimeOffset deadline = _clock.GetUtcNow() + _timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.CheckLimits();
            if (session.IsAborted)
            {
                return Finish(OptimizationSessionStatus.Failed, assessment: null, duties, session.AbortDetail);
            }

            DateTimeOffset now = _clock.GetUtcNow();
            HardwareSnapshot snapshot = _hardware.ReadSnapshot() with { CapturedAt = now };
            if (PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature) is null)
            {
                return Finish(
                    OptimizationSessionStatus.Failed,
                    HoldAssessment.TelemetryLost,
                    duties,
                    "CPU temperature disappeared. Fans were not turned down.");
            }

            ThermalAbortReason? ceiling = SafetyLimits.Evaluate(snapshot);
            if (ceiling is not null)
            {
                return Finish(
                    OptimizationSessionStatus.Failed,
                    HoldAssessment.Aborted,
                    duties,
                    SafetyLimits.Describe(ceiling.Value));
            }

            if (ObservedDuty(snapshot, fanGroupId) != dutyPercent)
            {
                return Finish(
                    OptimizationSessionStatus.Failed,
                    assessment: null,
                    duties,
                    "The commanded duty did not stick. This is not a pass.");
            }

            snapshots.Add(snapshot);
            bool timedOut = now >= deadline;
            HoldAssessment assessment = HoldAssessor.Evaluate(
                snapshots,
                timedOut,
                fanGroupId,
                dutyCommanded: true);
            if (assessment == HoldAssessment.SettledMeasured)
            {
                return Finish(OptimizationSessionStatus.Passed, assessment, duties, detail: null);
            }

            if (assessment != HoldAssessment.TransientModeled)
            {
                return Finish(OptimizationSessionStatus.Failed, assessment, duties, assessment.ToString());
            }

            await _delay(_samplePeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private static int? ObservedDuty(HardwareSnapshot snapshot, string fanGroupId)
    {
        foreach (FanGroup group in snapshot.FanGroups)
        {
            if (string.Equals(group.Id, fanGroupId, StringComparison.Ordinal))
            {
                return group.DutyCyclePercent;
            }
        }

        return null;
    }

    private static OptimizationSessionResult Finish(
        OptimizationSessionStatus status,
        HoldAssessment? assessment,
        List<int> duties,
        string? detail) =>
        new(Guid.NewGuid(), status, assessment, SoftwareControlReleased: false, duties, detail);
}
