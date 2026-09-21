namespace AutoFan.Core;

public sealed record BaselineSchedule(
    TimeSpan Idle,
    TimeSpan Everyday,
    TimeSpan Low,
    TimeSpan High,
    TimeSpan Cooldown,
    TimeSpan SamplePeriod)
{
    public static BaselineSchedule Default { get; } = new(
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(1));
}
