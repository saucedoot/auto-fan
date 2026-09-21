namespace AutoFan.Core;

public sealed record CoolingPreferences(
    double QuietCool,
    double? CpuTargetCelsius,
    double? GpuTargetCelsius,
    double? CpuAbortCelsius = null,
    double? GpuAbortCelsius = null,
    string? PreferredGpuId = null,
    bool HideDisconnectedFans = false,
    FanPresence? Presence = null)
{
    public const double DefaultQuietCool = 0.5;
    public const double DefaultCpuTargetCelsius = 80;
    public const double DefaultGpuTargetCelsius = 75;
    public const double MinTargetCelsius = 40;

    public static CoolingPreferences Default { get; } = new(
        DefaultQuietCool,
        DefaultCpuTargetCelsius,
        DefaultGpuTargetCelsius,
        SafetyLimits.CpuAbortCelsius,
        SafetyLimits.GpuAbortCelsius,
        Presence: FanPresence.None);

    public FanPresence ConnectedFans => Presence ?? FanPresence.None;

    public double Blend => Math.Clamp(QuietCool, 0, 1);

    public ThermalAbortLimits AbortLimits =>
        ThermalAbortLimits.FromUser(CpuAbortCelsius, GpuAbortCelsius);

    public double CpuAbort => AbortLimits.CpuCelsius;

    public double GpuAbort => AbortLimits.GpuCelsius;

    public double CpuTarget => ClampCpu(CpuTargetCelsius ?? DefaultCpuTargetCelsius, CpuAbort);

    public double GpuTarget => ClampGpu(GpuTargetCelsius ?? DefaultGpuTargetCelsius, GpuAbort);

    public static double ClampCpu(double value, double abortCelsius) =>
        Math.Clamp(value, MinTargetCelsius, Math.Max(MinTargetCelsius, abortCelsius - 1));

    public static double ClampGpu(double value, double abortCelsius) =>
        Math.Clamp(value, MinTargetCelsius, Math.Max(MinTargetCelsius, abortCelsius - 1));
}
