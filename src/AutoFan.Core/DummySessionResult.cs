namespace AutoFan.Core;

public sealed record DummySessionResult(
    Guid Id,
    DateTimeOffset CompletedAt,
    DummySessionStatus Status,
    HardwareSnapshot Snapshot,
    string? Detail);
