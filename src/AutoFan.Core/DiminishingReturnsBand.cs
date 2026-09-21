namespace AutoFan.Core;

public sealed record DiminishingReturnsBand(
    string FanGroupId,
    string FanGroupName,
    InfluenceTarget Target,
    double? UsefulRpmMin,
    double? UsefulRpmMax,
    double? WastedRpmMin,
    double? WastedRpmMax,
    double? RecommendedRpm,
    MetricEvidence Evidence,
    string Reason);
