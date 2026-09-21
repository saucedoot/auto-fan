namespace AutoFan.Core;

public sealed record SkippedFanGroup(
    string FanGroupId,
    string FanGroupName,
    string Reason);
