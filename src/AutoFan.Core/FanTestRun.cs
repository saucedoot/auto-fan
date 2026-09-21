namespace AutoFan.Core;

public sealed record FanTestRun(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    FanTestRunStatus Status,
    string? AbortDetail,
    bool GpuLoadAvailable,
    IReadOnlyList<FanTestSample> Samples,
    IReadOnlyList<InfluenceEntry> Influence,
    IReadOnlyList<SkippedFanGroup> Skipped);
