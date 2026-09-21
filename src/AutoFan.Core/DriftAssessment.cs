namespace AutoFan.Core;

public sealed record DriftAssessment(
    bool Stale,
    string? Reason,
    DriftAction Action,
    IReadOnlyList<string> RetestGroupIds,
    bool AmbientShifted = false,
    bool Missed = false,
    bool AddedAirflow = false,
    double? AmbientCelsius = null)
{
    public static DriftAssessment Fresh { get; } =
        new(false, Reason: null, DriftAction.None, []);
}
