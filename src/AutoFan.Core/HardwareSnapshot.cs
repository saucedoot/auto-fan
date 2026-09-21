namespace AutoFan.Core;

public sealed record HardwareSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<SensorReading> Sensors,
    IReadOnlyList<FanGroup> FanGroups,
    bool IsDemoHardware);
