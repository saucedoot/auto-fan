namespace AutoFan.Core;

public sealed record FanPresence(
    bool Completed,
    IReadOnlyList<string> ConnectedIds,
    IReadOnlyList<string> EmptyIds)
{
    public static FanPresence None { get; } = new(false, [], []);

    public static FanPresence Ready { get; } = new(true, [], []);

    public bool IsEmpty(string id) =>
        Completed && EmptyIds.Contains(id, StringComparer.Ordinal);

    public bool IsConnected(string id) =>
        Completed && ConnectedIds.Contains(id, StringComparer.Ordinal);
}

public sealed record FanPresenceProgress(string Message);

public enum FanPresenceStatus
{
    Completed,
    Aborted,
    Cancelled,
}

public sealed record FanPresenceReport(
    FanPresenceStatus Status,
    IReadOnlyList<string> ConnectedIds,
    IReadOnlyList<string> EmptyIds,
    IReadOnlyList<SkippedFanGroup> Skipped,
    string? Detail)
{
    public FanPresence ToState() =>
        Status == FanPresenceStatus.Completed
            ? new FanPresence(true, ConnectedIds, EmptyIds)
            : FanPresence.None;
}
