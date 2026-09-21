namespace AutoFan.Core;

public sealed record MeasuredRpmPlot(
    string? FanGroupId,
    string FanGroupName,
    InfluenceTarget Target,
    IReadOnlyList<FanSpeedPoint> Points,
    double? RecommendedRpm,
    double? UsefulRpmMin,
    double? UsefulRpmMax,
    double? WastedRpmMin,
    double? WastedRpmMax,
    double RpmMin,
    double RpmMax,
    double TempMin,
    double TempMax,
    string EmptyReason)
{
    public const string NeedFanTestsReason = "Need fan tests first.";
    public const string HowToText =
        "Each dot is one test: we held this fan at that speed until temperature stopped moving.";
    public const string PlateauCallout = "Faster than this, cooler by less than 1 °C";
    public const double AxisPadFraction = 0.08;
    public const double MinRpmSpan = 100;
    public const double MinTempSpan = 2;

    public static IReadOnlyList<MeasuredRpmTargetChoice> TargetChoices { get; } =
    [
        new(InfluenceTarget.Cpu, InteractionBuilder.TargetName(InfluenceTarget.Cpu)),
        new(InfluenceTarget.Gpu, InteractionBuilder.TargetName(InfluenceTarget.Gpu)),
    ];

    public bool HasPoints => Points.Count > 0;

    public string Title =>
        HasPoints
            ? $"{FanGroupName}  ·  {InteractionBuilder.TargetName(Target)}"
            : "Measured RPM vs temperature";

    public string HowTo => HasPoints ? HowToText : EmptyReason;

    public string Caption
    {
        get
        {
            if (!HasPoints)
            {
                return EmptyReason;
            }

            if (RecommendedRpm is double rpm)
            {
                return $"Left is slower and hotter. Right is faster. The dashed line is the quietest speed within 1 °C of the coolest result ({rpm:0} RPM). The green band is extra speed that barely cooled more.";
            }

            return "Left is slower and hotter. Right is faster. Each dot is a settled temperature at that fan speed.";
        }
    }

    public string RecommendedLabel =>
        RecommendedRpm is double rpm ? $"Recommended  {rpm:0} RPM" : string.Empty;

    public static IReadOnlyList<MeasuredRpmFanChoice> FanChoices(IReadOnlyList<FanSpeedCurve> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);

        var choices = new List<MeasuredRpmFanChoice>();
        foreach (FanSpeedCurve curve in curves)
        {
            if (!IsPlotTarget(curve.Target)
                || curve.Points.Count == 0
                || choices.Any(choice => string.Equals(choice.FanGroupId, curve.FanGroupId, StringComparison.Ordinal)))
            {
                continue;
            }

            choices.Add(new MeasuredRpmFanChoice(curve.FanGroupId, curve.FanGroupName));
        }

        return choices;
    }

    public static MeasuredRpmPlot For(
        IReadOnlyList<FanSpeedCurve> curves,
        DiminishingReturnsReport? report,
        string? groupId,
        InfluenceTarget target)
    {
        ArgumentNullException.ThrowIfNull(curves);

        if (!IsPlotTarget(target))
        {
            return Empty(target, NeedFanTestsReason);
        }

        FanSpeedCurve? curve = FindCurve(curves, groupId, target);
        if (curve is null)
        {
            return Empty(
                target,
                FanChoices(curves).Count == 0
                    ? NeedFanTestsReason
                    : $"No measured {InteractionBuilder.TargetName(target)} points for this fan.");
        }

        var points = new List<FanSpeedPoint>(curve.Points.Count);
        foreach (FanSpeedPoint point in curve.Points)
        {
            if (!double.IsFinite(point.Rpm)
                || point.Rpm <= 0
                || !double.IsFinite(point.TempCelsius))
            {
                continue;
            }

            points.Add(point);
        }

        points.Sort(static (left, right) => left.Rpm.CompareTo(right.Rpm));
        if (points.Count == 0)
        {
            return Empty(target, NeedFanTestsReason);
        }

        DiminishingReturnsBand? band = FindBand(report, curve.FanGroupId, target);
        (double rpmMin, double rpmMax) = Pad(
            points[0].Rpm,
            points[^1].Rpm,
            MinRpmSpan);
        (double tempMin, double tempMax) = Pad(
            points.Min(static point => point.TempCelsius),
            points.Max(static point => point.TempCelsius),
            MinTempSpan);
        return new MeasuredRpmPlot(
            curve.FanGroupId,
            curve.FanGroupName,
            target,
            points,
            band?.RecommendedRpm,
            band?.UsefulRpmMin,
            band?.UsefulRpmMax,
            band?.WastedRpmMin,
            band?.WastedRpmMax,
            rpmMin,
            rpmMax,
            tempMin,
            tempMax,
            EmptyReason: string.Empty);
    }

    public static MeasuredRpmPlot Empty(InfluenceTarget target, string reason) =>
        new(
            FanGroupId: null,
            FanGroupName: string.Empty,
            target,
            [],
            RecommendedRpm: null,
            UsefulRpmMin: null,
            UsefulRpmMax: null,
            WastedRpmMin: null,
            WastedRpmMax: null,
            RpmMin: 0,
            RpmMax: 1,
            TempMin: 0,
            TempMax: 1,
            reason);

    public bool IsOnPlateau(FanSpeedPoint point)
    {
        if (!HasPoints)
        {
            return false;
        }

        double best = Points.Min(static item => item.TempCelsius);
        return point.TempCelsius - best <= DiminishingReturnsAnalyzer.NegligibleGainCelsius;
    }

    private static bool IsPlotTarget(InfluenceTarget target) =>
        target is InfluenceTarget.Cpu or InfluenceTarget.Gpu;

    private static FanSpeedCurve? FindCurve(
        IReadOnlyList<FanSpeedCurve> curves,
        string? groupId,
        InfluenceTarget target)
    {
        FanSpeedCurve? fallback = null;
        foreach (FanSpeedCurve curve in curves)
        {
            if (curve.Target != target || curve.Points.Count == 0)
            {
                continue;
            }

            fallback ??= curve;
            if (groupId is not null
                && string.Equals(curve.FanGroupId, groupId, StringComparison.Ordinal))
            {
                return curve;
            }
        }

        return groupId is null ? fallback : null;
    }

    private static DiminishingReturnsBand? FindBand(
        DiminishingReturnsReport? report,
        string groupId,
        InfluenceTarget target)
    {
        if (report is null)
        {
            return null;
        }

        foreach (DiminishingReturnsBand band in report.Bands)
        {
            if (band.Target == target
                && string.Equals(band.FanGroupId, groupId, StringComparison.Ordinal))
            {
                return band;
            }
        }

        return null;
    }

    private static (double Min, double Max) Pad(double min, double max, double minSpan)
    {
        double span = max - min;
        if (span < minSpan)
        {
            double mid = (min + max) / 2;
            return (mid - (minSpan / 2), mid + (minSpan / 2));
        }

        double pad = span * AxisPadFraction;
        return (min - pad, max + pad);
    }
}
