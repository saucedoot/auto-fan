namespace AutoFan.Core;

public sealed record InteractionEntry(
    string FirstGroupId,
    string FirstGroupName,
    string SecondGroupId,
    string SecondGroupName,
    InfluenceTarget Target,
    double? FirstDeltaCelsius,
    double? SecondDeltaCelsius,
    double? CombinedDeltaCelsius,
    double? ResidualCelsius,
    MetricEvidence Evidence,
    string? InferredNote);
