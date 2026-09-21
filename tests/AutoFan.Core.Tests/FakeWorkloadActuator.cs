using AutoFan.Core;

namespace AutoFan.Core.Tests;

internal sealed class FakeWorkloadActuator : IWorkloadActuator
{
    public WorkloadLevel Current { get; private set; } = WorkloadLevel.Idle;

    public IReadOnlyList<WorkloadLevel> History => _history;

    public IReadOnlyList<HeatProfile> AppliedLow => _applied;

    public int StopCount { get; private set; }

    public bool GpuLoadAvailable { get; set; } = true;

    public HeatProfile? LockedLow { get; private set; }

    public bool HasFault { get; set; }

    public Action<HeatProfile>? OnApplyLow { get; set; }

    private readonly List<WorkloadLevel> _history = [];
    private readonly List<HeatProfile> _applied = [];

    public void Set(WorkloadLevel level)
    {
        Current = level;
        _history.Add(level);
        if (level == WorkloadLevel.Low)
        {
            HeatProfile profile = LockedLow ?? HeatProfile.DefaultLow;
            LockedLow = profile;
            _applied.Add(profile);
            OnApplyLow?.Invoke(profile);
        }
    }

    public void ApplyLow(HeatProfile profile)
    {
        Current = WorkloadLevel.Low;
        LockedLow = profile;
        _applied.Add(profile);
        OnApplyLow?.Invoke(profile);
    }

    public void Stop()
    {
        Current = WorkloadLevel.Idle;
        StopCount++;
    }

    public void Dispose() => Stop();
}
