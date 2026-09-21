namespace AutoFan.Core;

public sealed record InteractionRun(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    FanTestRunStatus Status,
    string? AbortDetail,
    bool GpuLoadAvailable,
    IReadOnlyList<InteractionSample> Samples,
    IReadOnlyList<InteractionEntry> Effects,
    IReadOnlyList<SkippedFanGroup> Skipped);
