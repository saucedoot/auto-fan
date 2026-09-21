namespace AutoFan.Core;

public sealed record FanTestSchedule(TimeSpan Timeout, TimeSpan SamplePeriod)
{
    public const int DutyStepPercent = 25;

    public static readonly int[] ScreenDuties = [40, 70, 100];

    public static readonly int[] RefineDuties = [55, 85];

    public static FanTestSchedule Default { get; } = new(
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(1));
}
