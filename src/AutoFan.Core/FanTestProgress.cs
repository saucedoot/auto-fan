namespace AutoFan.Core;

public sealed record FanTestProgress(
    string FanGroupId,
    string FanGroupName,
    int GroupIndex,
    int GroupCount,
    FanTestStage Stage,
    HardwareSnapshot Latest,
    string Message);
