namespace AutoFan.Core;

public sealed record InteractionProgress(
    HardwareSnapshot Latest,
    string Message);
