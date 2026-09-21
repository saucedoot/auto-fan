namespace AutoFan.Core;

public sealed record GroupPolicy(
    string FanGroupId,
    string FanGroupName,
    int QuietDutyPercent,
    int CoolDutyPercent,
    int DutyPercent,
    double? AppliedRpm);
