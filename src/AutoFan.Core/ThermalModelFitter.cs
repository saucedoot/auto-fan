namespace AutoFan.Core;

public static class ThermalModelFitter
{
    public static ThermalModel Fit(
        HardwareIdentity identity,
        BaselineRun? baseline,
        FanTestRun? fanTest,
        InteractionRun? interaction)
    {
        ArgumentNullException.ThrowIfNull(identity);

        IReadOnlyList<InfluenceEntry> influence = fanTest?.Influence ?? [];
        IReadOnlyList<InteractionEntry> effects = interaction?.Effects ?? [];
        IReadOnlyList<string> measuredGroups = MeasuredGroupIds(influence);
        IReadOnlyList<InteractionEntry> usableResiduals = UsableResiduals(effects, measuredGroups);
        (ModelConfidence confidence, string reason) = Score(
            measuredGroups.Count,
            usableResiduals.Count,
            fanTest?.Status);

        return new ThermalModel(
            identity.CpuName ?? "unknown",
            identity.GpuName ?? "unknown",
            identity.MotherboardName ?? "unknown",
            confidence,
            reason,
            baseline,
            influence,
            effects,
            fanTest?.Status,
            MeanTestTemp(fanTest, SensorKind.CpuTemperature),
            MeanTestTemp(fanTest, SensorKind.GpuTemperature));
    }

    public static string DescribeRelationship(InfluenceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return $"{entry.FanGroupName} {DescribeEffect(entry.Effect)} {InteractionBuilder.TargetName(entry.Target)}";
    }

    public static string DescribeEffect(InfluenceEffect? effect) =>
        effect switch
        {
            InfluenceEffect.None => "does little for",
            InfluenceEffect.Low => "slightly affects",
            InfluenceEffect.Medium => "affects",
            InfluenceEffect.High => "strongly affects",
            InfluenceEffect.VeryHigh => "very strongly affects",
            _ => "affects",
        };

    public static string DescribeConfidence(ModelConfidence confidence) =>
        confidence switch
        {
            ModelConfidence.None => "none",
            ModelConfidence.Low => "low",
            ModelConfidence.Medium => "medium",
            ModelConfidence.High => "high",
            _ => "none",
        };

    private static (ModelConfidence Confidence, string Reason) Score(
        int measuredGroupCount,
        int usableResidualCount,
        FanTestRunStatus? fanTestStatus)
    {
        if (measuredGroupCount == 0)
        {
            return (
                ModelConfidence.None,
                "Run fan tests first. There is not enough measured data to model this PC.");
        }

        if (measuredGroupCount == 1)
        {
            return (
                ModelConfidence.Low,
                "Only one fan group has a measured temperature change.");
        }

        if (fanTestStatus != FanTestRunStatus.Completed)
        {
            return (
                ModelConfidence.Low,
                "Fan tests did not finish, so confidence stays low.");
        }

        if (usableResidualCount == 0)
        {
            return (
                ModelConfidence.Medium,
                "Two or more fans were measured, but no pair leftover was recorded.");
        }

        return (
            ModelConfidence.Medium,
            "Fan tests finished and at least one pair leftover was measured. Confidence stays medium because those tests used a lighter heat, not a hotter load.");
    }

    private static double? MeanTestTemp(FanTestRun? fanTest, SensorKind kind)
    {
        if (fanTest is null)
        {
            return null;
        }

        var values = new List<double>();
        foreach (FanTestSample sample in fanTest.Samples)
        {
            if (!InfluenceMapBuilder.IsSpeedStage(sample.Stage))
            {
                continue;
            }

            if (PreferredTemperature.Read(sample.Snapshot, kind) is double value)
            {
                values.Add(value);
            }
        }

        return values.Count == 0 ? null : values.Average();
    }

    private static IReadOnlyList<string> MeasuredGroupIds(IReadOnlyList<InfluenceEntry> influence)
    {
        var ids = new List<string>();
        foreach (InfluenceEntry entry in influence)
        {
            if (entry.Evidence != MetricEvidence.Measured
                || entry.DeltaCelsius is null
                || ids.Contains(entry.FanGroupId, StringComparer.Ordinal))
            {
                continue;
            }

            ids.Add(entry.FanGroupId);
        }

        return ids;
    }

    private static IReadOnlyList<InteractionEntry> UsableResiduals(
        IReadOnlyList<InteractionEntry> effects,
        IReadOnlyList<string> measuredGroups)
    {
        var residuals = new List<InteractionEntry>();
        foreach (InteractionEntry entry in effects)
        {
            if (entry.Evidence != MetricEvidence.Measured || entry.ResidualCelsius is null)
            {
                continue;
            }

            if (!measuredGroups.Contains(entry.FirstGroupId, StringComparer.Ordinal)
                || !measuredGroups.Contains(entry.SecondGroupId, StringComparer.Ordinal))
            {
                continue;
            }

            residuals.Add(entry);
        }

        return residuals;
    }
}
