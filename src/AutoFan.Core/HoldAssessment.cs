namespace AutoFan.Core;

/// <summary>
/// Why a hold was accepted or rejected. Only <see cref="SettledMeasured"/>
/// may be treated as a measured fan effect.
/// </summary>
public enum HoldAssessment
{
    SettledMeasured,
    TransientModeled,
    TimedOut,
    PowerUnstable,
    Throttled,
    TelemetryLost,
    FanStalled,
    Aborted,
}
