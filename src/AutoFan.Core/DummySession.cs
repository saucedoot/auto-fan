namespace AutoFan.Core;

/// <summary>
/// A hardware-free observation pass used to prove Core wiring. This is not
/// an experiment planner and must not drive real fans.
/// </summary>
public sealed class DummySession
{
    private readonly IHardwareBackend _hardware;
    private readonly ISessionStore _store;

    public DummySession(IHardwareBackend hardware, ISessionStore store)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public DummySessionResult Run()
    {
        HardwareSnapshot snapshot = _hardware.ReadSnapshot();
        ThermalAbortReason? abort = SafetyLimits.Evaluate(snapshot);
        if (abort is not null)
        {
            return Persist(DummySessionStatus.Aborted, snapshot, SafetyLimits.Describe(abort.Value));
        }

        return Persist(DummySessionStatus.Observed, snapshot, detail: null);
    }

    public DummySessionResult ProposeDuty(string fanGroupId, int percent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);

        HardwareSnapshot before = _hardware.ReadSnapshot();
        ThermalAbortReason? abort = SafetyLimits.Evaluate(before);
        if (abort is not null)
        {
            return Persist(DummySessionStatus.Aborted, before, SafetyLimits.Describe(abort.Value));
        }

        if (!DutyPercent.TryCreate(percent, out _))
        {
            return Persist(
                DummySessionStatus.DutyRejected,
                before,
                $"Duty cycle {percent}% is outside {SafetyLimits.MinDutyPercent}-{SafetyLimits.MaxDutyPercent}%.");
        }

        DutySetResult set = _hardware.TrySetDuty(fanGroupId, percent);
        if (!set.Accepted)
        {
            HardwareSnapshot unchanged = _hardware.ReadSnapshot();
            return Persist(DummySessionStatus.DutyRejected, unchanged, set.Error);
        }

        HardwareSnapshot after = _hardware.ReadSnapshot();
        return Persist(DummySessionStatus.DutyApplied, after, detail: null);
    }

    private DummySessionResult Persist(
        DummySessionStatus status,
        HardwareSnapshot snapshot,
        string? detail)
    {
        var result = new DummySessionResult(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            status,
            snapshot,
            detail);
        _store.Save(result);
        return result;
    }
}
