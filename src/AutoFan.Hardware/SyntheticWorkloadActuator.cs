using AutoFan.Core;

namespace AutoFan.Hardware;

public sealed class SyntheticWorkloadActuator : IWorkloadActuator
{
    private readonly CpuWorkload _cpu = new();
    private readonly object _gate = new();
    private GpuWorkload? _gpu;
    private HeatProfile? _lockedEveryday;
    private HeatProfile? _lockedLow;
    private WorkloadLevel _level = WorkloadLevel.Idle;
    private bool _disposed;

    public SyntheticWorkloadActuator(string? preferredGpuName = null)
    {
        _gpu = GpuWorkload.TryCreate(preferredGpuName);
        GpuLoadAvailable = _gpu is not null;
    }

    public bool GpuLoadAvailable { get; private set; }

    public HeatProfile? LockedEveryday
    {
        get
        {
            lock (_gate)
            {
                return _lockedEveryday;
            }
        }
    }

    public HeatProfile? LockedLow
    {
        get
        {
            lock (_gate)
            {
                return _lockedLow;
            }
        }
    }

    public bool HasFault
    {
        get
        {
            lock (_gate)
            {
                return _gpu?.HasFault == true;
            }
        }
    }

    public void PreferGpu(string? name)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_level != WorkloadLevel.Idle)
            {
                return;
            }

            _gpu?.Dispose();
            _gpu = GpuWorkload.TryCreate(name);
            GpuLoadAvailable = _gpu is not null;
        }
    }

    public void Set(WorkloadLevel level)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (level == WorkloadLevel.Everyday)
            {
                _lockedEveryday = HeatProfile.Everyday;
            }

            if (level == WorkloadLevel.Low)
            {
                if (_lockedLow is null)
                {
                    throw new InvalidOperationException(HeatProfile.MissingLampDetail);
                }

                ApplyLowLocked(_lockedLow);
                return;
            }

            _cpu.Set(level);
            _gpu?.Set(level);
            _level = level;
        }
    }

    public void ApplyLow(HeatProfile profile)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ApplyLowLocked(profile);
        }
    }

    public void ApplyEveryday(HeatProfile profile)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _lockedEveryday = profile;
            _cpu.SetWorkers(profile.CpuWorkers);
            _gpu?.Set(profile);
            _level = WorkloadLevel.Everyday;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cpu.Stop();
            _gpu?.Stop();
            _level = WorkloadLevel.Idle;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _cpu.Dispose();
            _gpu?.Dispose();
            _disposed = true;
        }
    }

    private void ApplyLowLocked(HeatProfile profile)
    {
        _lockedLow = profile;
        _cpu.SetWorkers(profile.CpuWorkers);
        _gpu?.Set(profile);
        _level = WorkloadLevel.Low;
    }
}
