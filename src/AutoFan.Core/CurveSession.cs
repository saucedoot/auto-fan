namespace AutoFan.Core;

/// <summary>
/// Writes curve duties only for a validated profile, and restores on stop
/// or any decision that gives authority back. A draft never starts.
/// </summary>
public sealed class CurveSession : IDisposable
{
    private readonly IHardwareBackend _hardware;
    private readonly ICompetingSoftwareScanner _scanner;
    private readonly TimeProvider _clock;
    private readonly ThermalAbortLimits _limits;
    private readonly ActuatorMemory _memory = new();
    private readonly IReadOnlyDictionary<string, FanRunLimit> _runLimits;
    private SafeFanSession? _session;
    private CoolingProfile? _profile;

    public CurveSession(
        IHardwareBackend hardware,
        ICompetingSoftwareScanner scanner,
        TimeProvider? clock = null,
        ThermalAbortLimits? limits = null,
        IReadOnlyDictionary<string, FanRunLimit>? runLimits = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _clock = clock ?? TimeProvider.System;
        _limits = limits ?? ThermalAbortLimits.Floor;
        _runLimits = runLimits ?? new Dictionary<string, FanRunLimit>(StringComparer.Ordinal);
    }

    public bool IsRunning => _profile is not null;

    public bool TryStart(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Stop();
        if (!CurveActuator.MayTakeControl(profile))
        {
            _hardware.RestoreDefaults();
            return false;
        }

        _profile = profile;
        _session = new SafeFanSession(_hardware, _scanner, _clock, limits: _limits);
        return true;
    }

    public ActuatorDecision Tick(HardwareSnapshot snapshot, bool gpuFault = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_profile is null || _session is null)
        {
            return CurveActuator.RestoreNow(CurveActuator.DraftStaysOff);
        }

        ActuatorDecision decision = CurveActuator.Next(_profile, _memory, snapshot, gpuFault, _runLimits);
        if (decision.Restore)
        {
            Stop();
            return decision;
        }

        foreach (ActuatorCommand command in decision.Commands)
        {
            DutySetResult write = _session.TrySetDuty(command.GroupId, command.DutyPercent);
            if (_session.IsAborted || !write.Accepted)
            {
                string reason = _session.AbortDetail ?? write.Error ?? "A fan command was rejected.";
                Stop();
                return CurveActuator.RestoreNow(reason);
            }
        }

        return decision;
    }

    public void Stop()
    {
        _session?.Dispose();
        _session = null;
        _profile = null;
        _hardware.RestoreDefaults();
    }

    public void Dispose() => Stop();
}
