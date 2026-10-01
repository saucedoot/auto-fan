namespace AutoFan.Core;

public sealed record HardwareIdentity(
    string DisplayName,
    string? CpuName,
    string? GpuName,
    string? MotherboardName,
    string? GpuHardwareId = null)
{
    public static HardwareIdentity Unknown { get; } = new("This PC", null, null, null);
}
