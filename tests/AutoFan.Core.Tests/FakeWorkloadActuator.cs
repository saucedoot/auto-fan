using AutoFan.Core;

namespace AutoFan.Core.Tests;

internal sealed class FakeWorkloadActuator : IWorkloadActuator
{
    public WorkloadLevel Current { get; private set; } = WorkloadLevel.Idle;

    public IReadOnlyList<WorkloadLevel> History => _history;

    public IReadOnlyList<HeatProfile> AppliedLow => _applied;

    public IReadOnlyList<HeatProfile> AppliedEveryday => _appliedEveryday;

    public IReadOnlyList<HeatProfile> AppliedHot => _appliedHot;

    public int StopCount { get; private set; }

    public bool GpuLoadAvailable { get; set; } = true;

    public HeatProfile? LockedEveryday { get; private set; }

    public HeatProfile? LockedLow { get; private set; }

    public HeatProfile? LockedHot { get; private set; }

    public bool HasFault { get; set; }

    public Action<HeatProfile>? OnApplyLow { get; set; }

    public Action<HeatProfile>? OnApplyHot { get; set; }

    private readonly List<WorkloadLevel> _history = [];
    private readonly List<HeatProfile> _applied = [];
    private readonly List<HeatProfile> _appliedEveryday = [];
    private readonly List<HeatProfile> _appliedHot = [];

    public void Set(WorkloadLevel level)
    {
        if (level == WorkloadLevel.Low)
        {
            if (LockedLow is null)
            {
                throw new InvalidOperationException(HeatProfile.MissingLampDetail);
            }

            ApplyLow(LockedLow);
            _history.Add(level);
            return;
        }

        if (level == WorkloadLevel.Everyday)
        {
            LockedEveryday = HeatProfile.Everyday;
        }

        Current = level;
        _history.Add(level);
    }

    public void ApplyLow(HeatProfile profile)
    {
        Current = WorkloadLevel.Low;
        LockedLow = profile;
        _applied.Add(profile);
        OnApplyLow?.Invoke(profile);
    }

    public void ApplyEveryday(HeatProfile profile)
    {
        Current = WorkloadLevel.Everyday;
        LockedEveryday = profile;
        _appliedEveryday.Add(profile);
        _history.Add(WorkloadLevel.Everyday);
    }

    public void ApplyHot(HeatProfile profile)
    {
        Current = WorkloadLevel.Low;
        LockedHot = profile;
        _appliedHot.Add(profile);
        OnApplyHot?.Invoke(profile);
    }

    public void DiscardHot()
    {
        LockedHot = null;
    }

    public void Stop()
    {
        Current = WorkloadLevel.Idle;
        StopCount++;
    }

    public void Dispose() => Stop();
}
