namespace AutoFan.Core;

public sealed record BaselineSample(
    DateTimeOffset CapturedAt,
    BaselinePhase Phase,
    HardwareSnapshot Snapshot);
