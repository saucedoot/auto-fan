namespace AutoFan.Core;

public sealed record OptimizationSessionResult(
    Guid Id,
    OptimizationSessionStatus Status,
    HoldAssessment? Assessment,
    bool SoftwareControlReleased,
    IReadOnlyList<int> SoftwareDuties,
    string? Detail);
