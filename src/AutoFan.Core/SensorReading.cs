namespace AutoFan.Core;

public sealed record SensorReading(
    string Id,
    string Name,
    SensorKind Kind,
    double? Value,
    string Unit);
