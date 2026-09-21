namespace AutoFan.Core;

public sealed record BaselineRun(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    BaselineRunStatus Status,
    string? AbortDetail,
    double? AmbientCelsius,
    bool GpuLoadAvailable,
    IReadOnlyList<BaselineSample> Samples,
    IReadOnlyList<BaselineMetric> Metrics,
    HeatProfile? EverydayProfile = null,
    HeatProfile? LowProfile = null);
