namespace AutoFan.Core;

public sealed class PolicySession : IDisposable
{
    public const int MaxDutyStepPercent = 5;
    public const double UnderTargetMarginCelsius = 5;
    public const double TestLoadHeadroomCelsius = 3;

    private readonly IHardwareBackend _hardware;
    private readonly CoolingPolicy _policy;
    private readonly SafeFanSession _session;
    private readonly PowerReference _power;
    private readonly IReadOnlyDictionary<string, WorkloadGroupRole> _roles;
    private readonly Dictionary<string, int> _current = new(StringComparer.Ordinal);
    private int _elevatedTicks;
    private bool _disposed;

    public PolicySession(
        IHardwareBackend hardware,
        ICompetingSoftwareScanner scanner,
        CoolingPolicy policy,
        TimeProvider? clock = null,
        ThermalModel? model = null,
        ThermalAbortLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(policy);

        _hardware = hardware;
        _policy = policy;
        _session = new SafeFanSession(hardware, scanner, clock, FanSessionKind.Policy, limits);
        _power = PowerReference.FromBaseline(model?.Baseline);
        _roles = model is null
            ? new Dictionary<string, WorkloadGroupRole>(StringComparer.Ordinal)
            : WorkloadGroupRoles.FromInfluence(model.Influence);
    }

    public bool IsAborted => _session.IsAborted;

    public string? AbortDetail => _session.AbortDetail;

    public bool IsActive { get; private set; }

    public DutySetResult Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (GroupPolicy group in _policy.Groups)
        {
            DutySetResult result = _session.TrySetDuty(group.FanGroupId, group.DutyPercent);
            if (!result.Accepted)
            {
                _session.Restore();
                _current.Clear();
                IsActive = false;
                return result;
            }

            _current[group.FanGroupId] = group.DutyPercent;
        }

        IsActive = true;
        return new DutySetResult(true, Error: null);
    }

    public void Tick()
    {
        if (_disposed || !IsActive)
        {
            return;
        }

        _session.CheckLimits();
        if (_session.IsAborted)
        {
            IsActive = false;
            _current.Clear();
            return;
        }

        HardwareSnapshot snapshot = _hardware.ReadSnapshot();
        UpdateWorkload(snapshot);
        WorkloadState state = CurrentState(snapshot);
        foreach (GroupPolicy group in _policy.Groups)
        {
            int direction = NudgeDirection(group, snapshot, state);
            if (direction == 0)
            {
                continue;
            }

            int current = _current.GetValueOrDefault(group.FanGroupId, group.DutyPercent);
            int bound = direction > 0 ? group.CoolDutyPercent : group.QuietDutyPercent;
            int next = current;
            if (direction > 0 && current < bound)
            {
                next = Math.Min(bound, current + MaxDutyStepPercent);
            }
            else if (direction < 0 && current > bound)
            {
                next = Math.Max(bound, current - MaxDutyStepPercent);
            }

            if (next == current)
            {
                continue;
            }

            DutySetResult result = _session.TrySetDuty(group.FanGroupId, next);
            if (!result.Accepted)
            {
                if (_session.IsAborted)
                {
                    IsActive = false;
                    _current.Clear();
                }

                return;
            }

            _current[group.FanGroupId] = next;
        }
    }

    public void CheckSafety()
    {
        if (_disposed || !IsActive)
        {
            return;
        }

        _session.CheckLimits();
        if (_session.IsAborted)
        {
            IsActive = false;
            _current.Clear();
        }
    }

    public WorkloadState ReadWorkload()
    {
        if (_disposed || !IsActive)
        {
            return WorkloadState.Unknown;
        }

        return CurrentState(_hardware.ReadSnapshot());
    }

    public PolicyProgress Status()
    {
        HardwareSnapshot snapshot = _hardware.ReadSnapshot();
        if (_session.IsAborted)
        {
            return new PolicyProgress(
                _session.AbortDetail ?? "Stopped by a safety limit. Fans are back on BIOS and the NVIDIA driver.",
                snapshot);
        }

        return new PolicyProgress(HoldMessage(snapshot), snapshot);
    }

    public int CurrentDuty(string fanGroupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);
        return _current.GetValueOrDefault(fanGroupId);
    }

    public DutySetResult AddAirflow()
    {
        if (_disposed || !IsActive)
        {
            return new DutySetResult(false, "Policy is not holding fan speeds.");
        }

        foreach (GroupPolicy group in _policy.Groups)
        {
            int current = _current.GetValueOrDefault(group.FanGroupId, group.DutyPercent);
            int next = Math.Min(SafetyLimits.MaxDutyPercent, current + PolicyConfirmer.ExtraDutyPercent);
            if (next == current)
            {
                continue;
            }

            DutySetResult result = _session.TrySetDuty(group.FanGroupId, next);
            if (!result.Accepted)
            {
                if (_session.IsAborted)
                {
                    IsActive = false;
                    _current.Clear();
                }

                return result;
            }

            _current[group.FanGroupId] = next;
        }

        return new DutySetResult(true, Error: null);
    }

    public void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _session.Restore();
        IsActive = false;
        _current.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _session.Dispose();
        IsActive = false;
        _current.Clear();
        _disposed = true;
    }

    private void UpdateWorkload(HardwareSnapshot snapshot)
    {
        WorkloadPattern mix = WorkloadClassifier.Mix(snapshot, _power);
        if (WorkloadClassifier.IsElevatedMix(mix))
        {
            _elevatedTicks++;
        }
        else
        {
            _elevatedTicks = 0;
        }
    }

    private WorkloadState CurrentState(HardwareSnapshot snapshot) =>
        WorkloadClassifier.Classify(snapshot, _power, _elevatedTicks);

    private int NudgeDirection(GroupPolicy group, HardwareSnapshot snapshot, WorkloadState state)
    {
        double? cpu = PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature);
        double? gpu = PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature);
        bool over = (cpu is double cpuValue && cpuValue > _policy.CpuTargetCelsius)
            || (gpu is double gpuValue && gpuValue > _policy.GpuTargetCelsius);
        if (over)
        {
            return 1;
        }

        bool hotterThanTest =
            (_policy.TestCpuCelsius is double testCpu && cpu is double liveCpu && liveCpu > testCpu + TestLoadHeadroomCelsius)
            || (_policy.TestGpuCelsius is double testGpu && gpu is double liveGpu && liveGpu > testGpu + TestLoadHeadroomCelsius);
        if (hotterThanTest)
        {
            return 1;
        }

        if (HotterThanTestPower(snapshot))
        {
            return 1;
        }

        WorkloadGroupRole role = _roles.GetValueOrDefault(group.FanGroupId);
        if (state.IsPowerLead && WorkloadGroupRoles.ShouldRaiseFromPower(role, state.Pattern))
        {
            return 1;
        }

        bool cpuUnder = cpu is null || cpu <= _policy.CpuTargetCelsius - UnderTargetMarginCelsius;
        bool gpuUnder = gpu is null || gpu <= _policy.GpuTargetCelsius - UnderTargetMarginCelsius;
        bool mayQuiet = state.Pattern is WorkloadPattern.Desktop or WorkloadPattern.Unknown
            && !WorkloadClassifier.IsElevatedMix(state.Pattern);
        return mayQuiet && cpuUnder && gpuUnder ? -1 : 0;
    }

    private bool HotterThanTestPower(HardwareSnapshot snapshot)
    {
        double? cpu = PreferredPower.Read(snapshot, SensorKind.CpuPower);
        double? gpu = PreferredPower.Read(snapshot, SensorKind.GpuPower);
        return (_power.TestCpuWatts is double testCpu && cpu is double liveCpu && liveCpu > testCpu)
            || (_power.TestGpuWatts is double testGpu && gpu is double liveGpu && liveGpu > testGpu);
    }

    private string HoldMessage(HardwareSnapshot snapshot)
    {
        string temps =
            $"CPU {FormatTemp(snapshot, SensorKind.CpuTemperature)}. GPU {FormatTemp(snapshot, SensorKind.GpuTemperature)}.";
        WorkloadState state = CurrentState(snapshot);
        return state.Pattern switch
        {
            WorkloadPattern.Desktop => $"Desktop — keeping fans quieter. {temps}",
            WorkloadPattern.Gaming when state.Duration == WorkloadDuration.Burst =>
                $"Gaming burst — waiting before raising fans from GPU power. {temps}",
            WorkloadPattern.Gaming =>
                $"Gaming — GPU power rose, raising GPU fans before the temperature spike. {temps}",
            WorkloadPattern.Render when state.Duration == WorkloadDuration.Burst =>
                $"Rendering burst — waiting before raising fans from CPU power. {temps}",
            WorkloadPattern.Render =>
                $"Rendering — CPU power rose, raising CPU fans before the temperature spike. {temps}",
            WorkloadPattern.Mixed when state.Duration == WorkloadDuration.Burst =>
                $"Mixed burst — waiting before raising fans from power. {temps}",
            WorkloadPattern.Mixed =>
                $"Mixed — CPU and GPU power rose, raising fans before the temperature spike. {temps}",
            _ => $"Holding the chosen fan speeds. {temps}",
        };
    }

    private static string FormatTemp(HardwareSnapshot snapshot, SensorKind kind) =>
        PreferredTemperature.Read(snapshot, kind) is double value
            ? $"{value:0.#} °C"
            : "unknown";
}
