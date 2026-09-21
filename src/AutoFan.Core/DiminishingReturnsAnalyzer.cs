namespace AutoFan.Core;

public static class DiminishingReturnsAnalyzer
{
    public const double NegligibleGainCelsius = 1.0;
    public const int MinimumPointCount = 3;

    public static DiminishingReturnsReport Analyze(IReadOnlyList<FanSpeedCurve> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);

        var bands = new List<DiminishingReturnsBand>(curves.Count);
        foreach (FanSpeedCurve curve in curves)
        {
            bands.Add(AnalyzeCurve(curve));
        }

        return new DiminishingReturnsReport(bands);
    }

    public static string DescribeRpm(double rpm) => $"{rpm:0} RPM";

    public static string DescribeRange(double minRpm, double maxRpm) =>
        minRpm == maxRpm
            ? DescribeRpm(minRpm)
            : $"{minRpm:0}–{maxRpm:0} RPM";

    private static DiminishingReturnsBand AnalyzeCurve(FanSpeedCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        IReadOnlyList<FanSpeedPoint> points = ValidSortedPoints(curve.Points);
        if (points.Count < MinimumPointCount)
        {
            return Unknown(
                curve,
                "Need at least three measured fan speeds to find a plateau.");
        }

        double bestTemp = points.Min(static point => point.TempCelsius);
        var plateau = new List<FanSpeedPoint>();
        foreach (FanSpeedPoint point in points)
        {
            if (point.TempCelsius - bestTemp <= NegligibleGainCelsius)
            {
                plateau.Add(point);
            }
        }

        if (plateau.Count == 0)
        {
            return Unknown(curve, "Need at least three measured fan speeds to find a plateau.");
        }

        FanSpeedPoint last = points[^1];
        bool highestIsPlateau = plateau[^1].Rpm == last.Rpm
            && plateau[^1].TempCelsius == last.TempCelsius;
        double recommended = plateau[0].Rpm;

        if (plateau.Count == 1 && highestIsPlateau)
        {
            return new DiminishingReturnsBand(
                curve.FanGroupId,
                curve.FanGroupName,
                curve.Target,
                recommended,
                recommended,
                WastedRpmMin: null,
                WastedRpmMax: null,
                recommended,
                curve.Evidence,
                "More speed still cools. There is no wasted band yet.");
        }

        if (highestIsPlateau && plateau.Count >= 2)
        {
            FanSpeedPoint usefulMax = plateau[^2];
            FanSpeedPoint wastedMin = plateau[^1];
            return new DiminishingReturnsBand(
                curve.FanGroupId,
                curve.FanGroupName,
                curve.Target,
                plateau[0].Rpm,
                usefulMax.Rpm,
                wastedMin.Rpm,
                last.Rpm,
                recommended,
                curve.Evidence,
                $"Almost all cooling is already there by {DescribeRpm(recommended)}. Faster than {DescribeRpm(usefulMax.Rpm)} buys little.");
        }

        FanSpeedPoint lastUseful = plateau[^1];
        FanSpeedPoint? firstWasted = FirstAfter(points, lastUseful.Rpm);
        return new DiminishingReturnsBand(
            curve.FanGroupId,
            curve.FanGroupName,
            curve.Target,
            plateau[0].Rpm,
            lastUseful.Rpm,
            firstWasted?.Rpm,
            firstWasted is null ? null : last.Rpm,
            recommended,
            curve.Evidence,
            firstWasted is null
                ? $"Use about {DescribeRpm(recommended)}."
                : $"Higher speeds after {DescribeRpm(lastUseful.Rpm)} run hotter or do not help.");
    }

    private static DiminishingReturnsBand Unknown(FanSpeedCurve curve, string reason) =>
        new(
            curve.FanGroupId,
            curve.FanGroupName,
            curve.Target,
            UsefulRpmMin: null,
            UsefulRpmMax: null,
            WastedRpmMin: null,
            WastedRpmMax: null,
            RecommendedRpm: null,
            MetricEvidence.Unknown,
            reason);

    private static IReadOnlyList<FanSpeedPoint> ValidSortedPoints(IReadOnlyList<FanSpeedPoint> points)
    {
        var valid = new List<FanSpeedPoint>();
        foreach (FanSpeedPoint point in points)
        {
            if (double.IsFinite(point.Rpm)
                && point.Rpm >= 0
                && double.IsFinite(point.TempCelsius))
            {
                valid.Add(point);
            }
        }

        valid.Sort(static (left, right) => left.Rpm.CompareTo(right.Rpm));
        return valid;
    }

    private static FanSpeedPoint? FirstAfter(IReadOnlyList<FanSpeedPoint> points, double rpm)
    {
        foreach (FanSpeedPoint point in points)
        {
            if (point.Rpm > rpm)
            {
                return point;
            }
        }

        return null;
    }
}
