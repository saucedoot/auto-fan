namespace AutoFan.Core;

public sealed record InteractionSample(
    DateTimeOffset CapturedAt,
    string FirstGroupId,
    string FirstGroupName,
    string SecondGroupId,
    string SecondGroupName,
    InteractionStep Step,
    HardwareSnapshot Snapshot,
    bool Settled = false);
