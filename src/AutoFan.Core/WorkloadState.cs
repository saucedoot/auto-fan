namespace AutoFan.Core;

public sealed record WorkloadState(WorkloadPattern Pattern, WorkloadDuration Duration)
{
    public static WorkloadState Unknown { get; } =
        new(WorkloadPattern.Unknown, WorkloadDuration.Sustained);

    public bool IsPowerLead =>
        Duration == WorkloadDuration.Sustained
        && Pattern is WorkloadPattern.Gaming or WorkloadPattern.Render or WorkloadPattern.Mixed;
}
