namespace AutoFan.Core;

public sealed record FanTestSchedule(TimeSpan Timeout, TimeSpan SamplePeriod)
{
    public const int DutyStepPercent = 25;

    public static readonly int[] ScreenDuties = [15, 30, 45, 60, 75, 90, 100];

    public static readonly int[] RefineDuties = [20, 40, 50, 70, 85];

    public static readonly int[] EverydayScreenDuties = [20, 40, 70, 100];

    public static readonly int[] EverydayRefineDuties = [55, 85];

    public static FanTestSchedule Default { get; } = new(
        TimeSpan.FromSeconds(90),
        TimeSpan.FromSeconds(1));

    public static IReadOnlyList<int> AbsoluteDuties(
        IReadOnlyList<int> duties,
        int biosDuty,
        IReadOnlySet<int>? alreadyCommanded = null)
    {
        ArgumentNullException.ThrowIfNull(duties);

        HashSet<int> seen = alreadyCommanded is null ? [] : [.. alreadyCommanded];
        var targets = new List<int>();
        foreach (int duty in duties.OrderByDescending(static value => value))
        {
            if (!SafetyLimits.IsDutyInRange(duty)
                || seen.Contains(duty)
                || targets.Contains(duty)
                || (duty == SafetyLimits.MaxDutyPercent
                    && biosDuty >= SafetyLimits.MaxDutyPercent))
            {
                continue;
            }

            targets.Add(duty);
        }

        return targets;
    }

    public static bool IsAlreadyAtMax(int biosDuty, IReadOnlyList<int> remainingDuties)
    {
        ArgumentNullException.ThrowIfNull(remainingDuties);
        return remainingDuties.Count == 0 && biosDuty >= SafetyLimits.MaxDutyPercent;
    }
}
