namespace AutoFan.Core;

/// <summary>
/// Read sensors and apply a duty change. Real writes must register a restore
/// path (motherboard BIOS and NVIDIA driver); <see cref="RestoreDefaults"/>
/// returns control.
/// </summary>
public interface IHardwareBackend
{
    string DisplayName { get; }

    bool IsDemo { get; }

    IReadOnlyList<FanGroup> FanGroups { get; }

    bool HasActiveSoftwareControl { get; }

    HardwareSnapshot ReadSnapshot();

    /// <summary>
    /// Attempts to store a duty cycle. Callers should pass a value already
    /// checked with <see cref="DutyPercent"/>; backends must still reject
    /// out-of-range percents so a missed Core check cannot stick.
    /// </summary>
    DutySetResult TrySetDuty(string fanGroupId, int percent);

    /// <summary>
    /// Returns motherboard headers to BIOS and NVIDIA GPU fans to the driver
    /// curve. Must be safe to call when nothing was written.
    /// </summary>
    void RestoreDefaults();
}
