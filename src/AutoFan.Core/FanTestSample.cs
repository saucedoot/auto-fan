namespace AutoFan.Core;

public sealed record FanTestSample(
    DateTimeOffset CapturedAt,
    string FanGroupId,
    string FanGroupName,
    FanTestStage Stage,
    HardwareSnapshot Snapshot,
    bool Settled = false);
