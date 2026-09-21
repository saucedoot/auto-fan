namespace AutoFan.Core;

public sealed record ThermalPrediction(
    InfluenceTarget Target,
    double? DeltaCelsius,
    MetricEvidence Evidence,
    PredictionReason Reason);
