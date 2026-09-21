namespace AutoFan.Core;

public sealed record PredictionBreakdown(
    InfluenceTarget Target,
    IReadOnlyList<PredictionContribution> Fans,
    IReadOnlyList<PredictionContribution> Pairs,
    double? TotalDeltaCelsius,
    MetricEvidence Evidence,
    PredictionReason Reason)
{
    public const string NoOtherCorrections = "none";

    public double FanTotalCelsius => Fans.Sum(static fan => fan.DeltaCelsius);

    public double PairTotalCelsius => Pairs.Sum(static pair => pair.DeltaCelsius);
}
