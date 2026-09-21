namespace AutoFan.Core;

/// <summary>
/// Starts and stops synthetic heat. Implementations must not write fans.
/// </summary>
public interface IWorkloadActuator : IDisposable
{
    bool GpuLoadAvailable { get; }

    HeatProfile? LockedLow { get; }

    bool HasFault { get; }

    void Set(WorkloadLevel level);

    void ApplyLow(HeatProfile profile);

    void Stop();
}
