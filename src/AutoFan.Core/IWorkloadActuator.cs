namespace AutoFan.Core;

/// <summary>
/// Starts and stops synthetic heat. Implementations must not write fans.
/// </summary>
public interface IWorkloadActuator : IDisposable
{
    bool GpuLoadAvailable { get; }

    HeatProfile? LockedEveryday { get; }

    HeatProfile? LockedLow { get; }

    bool HasFault { get; }

    /// <summary>
    /// Idle, Everyday, or Low. Low requires a frozen Watch profile
    /// (<see cref="ApplyLow"/> or a previous lock). It must not fall back to
    /// <see cref="HeatProfile.DefaultLow"/>.
    /// </summary>
    void Set(WorkloadLevel level);

    void ApplyLow(HeatProfile profile);

    void Stop();
}
