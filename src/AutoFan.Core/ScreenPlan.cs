namespace AutoFan.Core;

/// <summary>
/// A few duties for the reference-step-reference screen. The band is
/// provisional until a trace from this PC is reviewed.
/// </summary>
public static class ScreenPlan
{
    public static IReadOnlyList<int> TestDuties(FanActuation? actuation)
    {
        if (actuation?.MinimumStableDutyPercent is int low && low is > 0 and < 100)
        {
            int mid = low + ((100 - low) / 2);
            return Distinct(low, mid, 100);
        }

        return [40, 70, 100];
    }

    public static bool TemperatureReturned(double firstMean, double firstRange, double secondMean)
    {
        double band = Math.Max(HoldAssessor.MaxWindowRangeCelsius, firstRange);
        return Math.Abs(secondMean - firstMean) <= band;
    }

    private static int[] Distinct(params int[] duties)
    {
        var seen = new List<int>();
        foreach (int duty in duties)
        {
            if (duty is > 0 and <= 100 && !seen.Contains(duty))
            {
                seen.Add(duty);
            }
        }

        return seen.ToArray();
    }
}
