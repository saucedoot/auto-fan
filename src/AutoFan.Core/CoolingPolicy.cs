namespace AutoFan.Core;

public sealed record CoolingPolicy(
    IReadOnlyList<GroupPolicy> Groups,
    double QuietCool,
    double CpuTargetCelsius,
    double GpuTargetCelsius,
    string Summary,
    double? TestCpuCelsius = null,
    double? TestGpuCelsius = null);
