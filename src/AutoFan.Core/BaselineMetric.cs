namespace AutoFan.Core;

public sealed record BaselineMetric(
    string Name,
    double? Value,
    string Unit,
    MetricEvidence Evidence);
