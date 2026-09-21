namespace AutoFan.Core;

public sealed record FanSpeedCurve(
    string FanGroupId,
    string FanGroupName,
    InfluenceTarget Target,
    IReadOnlyList<FanSpeedPoint> Points,
    MetricEvidence Evidence);
