namespace AutoFan.Core;

/// <summary>
/// Per-run abort temperatures. Never higher than the hardcoded floor.
/// </summary>
public readonly record struct ThermalAbortLimits(double CpuCelsius, double GpuCelsius)
{
    public const int MinAbortCelsius = 65;

    public static ThermalAbortLimits Floor { get; } = new(
        SafetyLimits.CpuAbortCelsius,
        SafetyLimits.GpuAbortCelsius);

    public static ThermalAbortLimits FromUser(double? cpuCelsius, double? gpuCelsius) =>
        new(
            ClampCpu(cpuCelsius ?? SafetyLimits.CpuAbortCelsius),
            ClampGpu(gpuCelsius ?? SafetyLimits.GpuAbortCelsius));

    public static double ClampCpu(double value) =>
        Math.Clamp(value, MinAbortCelsius, SafetyLimits.CpuAbortCelsius);

    public static double ClampGpu(double value) =>
        Math.Clamp(value, MinAbortCelsius, SafetyLimits.GpuAbortCelsius);
}
