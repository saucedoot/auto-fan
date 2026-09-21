namespace AutoFan.Core;

public sealed record FanGroup(
    string Id,
    string Name,
    int? DutyCyclePercent,
    double? Rpm,
    string ControllerName = "",
    bool IsControllable = false,
    FanGroupKind Kind = FanGroupKind.Fan);
