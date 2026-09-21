namespace AutoFan.Core;

public sealed record BaselineProgress(
    BaselinePhase Phase,
    HardwareSnapshot Latest,
    string Message);
