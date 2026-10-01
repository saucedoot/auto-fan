namespace AutoFan.Core;

public sealed record DutyRpmPoint(int DutyPercent, double Rpm);

/// <summary>
/// How one fan group spins. This is the motor, not a temperature map.
/// A start duty is set only when the fan needs more duty to begin spinning
/// than it needs to keep spinning.
/// </summary>
public sealed record FanActuation(
    string FanGroupId,
    string FanGroupName,
    int? ReferenceDutyPercent,
    double? ReferenceRpm,
    int? MinimumStableDutyPercent,
    int? StartDutyPercent,
    double? MaximumRpm,
    bool TachometerRepeats,
    IReadOnlyList<DutyRpmPoint> Points);
