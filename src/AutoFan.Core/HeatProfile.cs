namespace AutoFan.Core;

/// <summary>
/// Frozen CPU/GPU work for a heat-lamp setting. Intensity is work we submit,
/// not the driver's utilization percentage.
/// </summary>
public sealed record HeatProfile(
    int CpuWorkers,
    int GpuWidth,
    int GpuHeight,
    int GpuPasses,
    int GpuIterations)
{
    public static int EverydayCpuWorkers { get; } = Math.Max(1, Environment.ProcessorCount / 4);

    public static HeatProfile Idle { get; } = new(0, 0, 0, 0, 0);

    public static HeatProfile Everyday { get; } = new(EverydayCpuWorkers, 1280, 720, 1, 16);

    public static HeatProfile StartingLow { get; } = new(EverydayCpuWorkers, 2560, 1440, 1, 64);

    public static HeatProfile DefaultLow { get; } = new(EverydayCpuWorkers, 2560, 1440, 4, 64);

    public int GpuWorkUnits => GpuPasses * GpuIterations;
}
