namespace AutoFan.Core;

public sealed record SensorStabilityResult(
    SensorKind Kind,
    SensorStability State,
    double? RangeCelsius,
    double MinimumDetectableCelsius,
    IReadOnlyList<double> Holds);
