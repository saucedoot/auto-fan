namespace AutoFan.Core;

public sealed record PolicyConfirmation(
    double? MeasuredCpuCelsius,
    double? MeasuredGpuCelsius,
    double? ExpectedCpuCelsius,
    double? ExpectedGpuCelsius,
    bool Missed,
    bool AddedAirflow,
    Guid? Id = null,
    DateTimeOffset? ObservedAt = null,
    double? AmbientCelsius = null);
