namespace AutoFan.Core;

public sealed record ThermalModel(
    string CpuName,
    string GpuName,
    string MotherboardName,
    ModelConfidence Confidence,
    string ConfidenceReason,
    BaselineRun? Baseline,
    IReadOnlyList<InfluenceEntry> Influence,
    IReadOnlyList<InteractionEntry> Interactions,
    FanTestRunStatus? FanTestStatus,
    double? TestCpuCelsius = null,
    double? TestGpuCelsius = null)
{
    public ThermalPrediction Predict(IReadOnlyList<string> groupIds, InfluenceTarget target)
    {
        PredictionBreakdown breakdown = Explain(groupIds, target);
        return new ThermalPrediction(target, breakdown.TotalDeltaCelsius, breakdown.Evidence, breakdown.Reason);
    }

    public PredictionBreakdown Explain(IReadOnlyList<string> groupIds, InfluenceTarget target)
    {
        ArgumentNullException.ThrowIfNull(groupIds);

        IReadOnlyList<string> groups = DistinctIds(groupIds);
        if (groups.Count == 0)
        {
            return UnknownBreakdown(target);
        }

        var fans = new List<PredictionContribution>(groups.Count);
        foreach (string groupId in groups)
        {
            if (SingleDelta(groupId, target) is not double delta)
            {
                continue;
            }

            fans.Add(new PredictionContribution(GroupName(groupId), delta));
        }

        if (fans.Count == 0)
        {
            return UnknownBreakdown(target);
        }

        var pairs = new List<PredictionContribution>();
        double predicted = fans.Sum(static fan => fan.DeltaCelsius);
        if (groups.Count == 2
            && fans.Count == 2
            && PairResidual(groups[0], groups[1], target) is double residual)
        {
            predicted += residual;
            pairs.Add(new PredictionContribution(
                $"{GroupName(groups[0])} + {GroupName(groups[1])}",
                residual));
        }

        PredictionReason reason = fans.Count == 1 && pairs.Count == 0
            ? PredictionReason.MeasuredSingle
            : pairs.Count > 0
                ? PredictionReason.MeasuredPair
                : PredictionReason.AdditiveEstimate;
        return new PredictionBreakdown(
            target,
            fans,
            pairs,
            predicted,
            MetricEvidence.Modeled,
            reason);
    }

    public IReadOnlyList<string> MeasuredGroupIds()
    {
        var ids = new List<string>();
        foreach (InfluenceEntry entry in Influence)
        {
            if (!IsMeasured(entry) || ids.Contains(entry.FanGroupId, StringComparer.Ordinal))
            {
                continue;
            }

            ids.Add(entry.FanGroupId);
        }

        return ids;
    }

    public string GroupName(string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        foreach (InfluenceEntry entry in Influence)
        {
            if (string.Equals(entry.FanGroupId, groupId, StringComparison.Ordinal))
            {
                return entry.FanGroupName;
            }
        }

        foreach (InteractionEntry entry in Interactions)
        {
            if (string.Equals(entry.FirstGroupId, groupId, StringComparison.Ordinal))
            {
                return entry.FirstGroupName;
            }

            if (string.Equals(entry.SecondGroupId, groupId, StringComparison.Ordinal))
            {
                return entry.SecondGroupName;
            }
        }

        return groupId;
    }

    public IReadOnlyList<(string FirstId, string SecondId)> MeasuredPairs()
    {
        var pairs = new List<(string FirstId, string SecondId)>();
        foreach (InteractionEntry entry in Interactions)
        {
            if (entry.Evidence != MetricEvidence.Measured || entry.ResidualCelsius is null)
            {
                continue;
            }

            if (pairs.Any(pair => SamePair(pair.FirstId, pair.SecondId, entry.FirstGroupId, entry.SecondGroupId)))
            {
                continue;
            }

            pairs.Add((entry.FirstGroupId, entry.SecondGroupId));
        }

        return pairs;
    }

    private double? SingleDelta(string groupId, InfluenceTarget target)
    {
        foreach (InfluenceEntry entry in Influence)
        {
            if (string.Equals(entry.FanGroupId, groupId, StringComparison.Ordinal)
                && entry.Target == target
                && IsMeasured(entry))
            {
                return entry.DeltaCelsius;
            }
        }

        return null;
    }

    private double? PairResidual(string firstId, string secondId, InfluenceTarget target)
    {
        foreach (InteractionEntry entry in Interactions)
        {
            if (entry.Target != target
                || entry.Evidence != MetricEvidence.Measured
                || entry.ResidualCelsius is not double residual)
            {
                continue;
            }

            if (SamePair(firstId, secondId, entry.FirstGroupId, entry.SecondGroupId))
            {
                return residual;
            }
        }

        return null;
    }

    private static bool IsMeasured(InfluenceEntry entry) =>
        entry.Evidence == MetricEvidence.Measured && entry.DeltaCelsius is not null;

    private static PredictionBreakdown UnknownBreakdown(InfluenceTarget target) =>
        new(target, [], [], TotalDeltaCelsius: null, MetricEvidence.Unknown, PredictionReason.Unknown);

    private static IReadOnlyList<string> DistinctIds(IReadOnlyList<string> groupIds)
    {
        var ids = new List<string>();
        foreach (string groupId in groupIds)
        {
            if (string.IsNullOrWhiteSpace(groupId) || ids.Contains(groupId, StringComparer.Ordinal))
            {
                continue;
            }

            ids.Add(groupId);
        }

        return ids;
    }

    private static bool SamePair(string firstA, string secondA, string firstB, string secondB) =>
        (string.Equals(firstA, firstB, StringComparison.Ordinal)
            && string.Equals(secondA, secondB, StringComparison.Ordinal))
        || (string.Equals(firstA, secondB, StringComparison.Ordinal)
            && string.Equals(secondA, firstB, StringComparison.Ordinal));
}
