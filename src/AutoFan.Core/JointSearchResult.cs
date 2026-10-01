namespace AutoFan.Core;

public sealed record DutyCommand(string GroupId, int DutyPercent);

public sealed record JointVector(
    IReadOnlyList<DutyCommand> Commands,
    double? PredictedCpuCelsius,
    double? PredictedGpuCelsius);

public sealed record JointAttempt(
    IReadOnlyList<DutyCommand> Commands,
    HoldAssessment Assessment,
    bool Accepted,
    bool LikelyQuieter,
    double? HighestEffort,
    double? TotalEffort,
    string? Leftover);

/// <summary>
/// A draft combination search. <see cref="Validated"/> stays false until a
/// later proof. This is not a curve.
/// </summary>
public sealed record JointSearchResult(
    string? AbortDetail,
    JointAttempt? Chosen,
    IReadOnlyList<JointAttempt> Attempts,
    string Detail)
{
    public bool Draft => AbortDetail is null;

    public bool Validated => false;
}
