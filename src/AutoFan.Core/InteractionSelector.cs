namespace AutoFan.Core;

public static class InteractionSelector
{
    public const int MaxPairs = 3;

    public static IReadOnlyList<(FanGroup First, FanGroup Second)> Select(
        IReadOnlyList<FanGroup> groups,
        IReadOnlyList<InfluenceEntry>? influence = null,
        FanPresence? presence = null)
    {
        ArgumentNullException.ThrowIfNull(groups);

        Dictionary<string, FanGroup> writable = groups
            .Where(fan =>
                FanWriteCandidates.IsWritableTestFan(fan)
                && FanWriteCandidates.HasUsableTachometer(fan, presence))
            .GroupBy(static fan => fan.Id, StringComparer.Ordinal)
            .ToDictionary(static grouping => grouping.Key, static grouping => grouping.First(), StringComparer.Ordinal);
        if (writable.Count < 2 || influence is null || influence.Count == 0)
        {
            return [];
        }

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var pairs = new Dictionary<string, (string FirstId, string SecondId)>(StringComparer.Ordinal);
        foreach (InfluenceTarget target in Enum.GetValues<InfluenceTarget>())
        {
            IReadOnlyList<(string Id, double Delta)> fans = EligibleOnTarget(influence, writable, target);
            for (int first = 0; first < fans.Count; first++)
            {
                for (int second = first + 1; second < fans.Count; second++)
                {
                    if (FanWriteCandidates.AreCoupled(
                        writable[fans[first].Id],
                        writable[fans[second].Id]))
                    {
                        continue;
                    }

                    string key = PairKey(fans[first].Id, fans[second].Id);
                    scores[key] = scores.GetValueOrDefault(key) + Math.Abs(fans[first].Delta) + Math.Abs(fans[second].Delta);
                    pairs[key] = Ordered(fans[first].Id, fans[second].Id);
                }
            }
        }

        return scores
            .OrderByDescending(static pair => pair.Value)
            .Take(MaxPairs)
            .Select(pair => (writable[pairs[pair.Key].FirstId], writable[pairs[pair.Key].SecondId]))
            .ToArray();
    }

    private static IReadOnlyList<(string Id, double Delta)> EligibleOnTarget(
        IReadOnlyList<InfluenceEntry> influence,
        Dictionary<string, FanGroup> writable,
        InfluenceTarget target)
    {
        var fans = new List<(string Id, double Delta)>();
        foreach (InfluenceEntry entry in influence)
        {
            if (entry.Target != target
                || entry.Evidence != MetricEvidence.Measured
                || entry.DeltaCelsius is not double delta
                || !writable.ContainsKey(entry.FanGroupId))
            {
                continue;
            }

            if (entry.Effect is InfluenceEffect.None && Math.Abs(delta) <= InfluenceMapBuilder.NoneBandCelsius)
            {
                continue;
            }

            if (Math.Abs(delta) <= InfluenceMapBuilder.NoneBandCelsius && entry.Effect is null)
            {
                continue;
            }

            if (fans.Any(existing => existing.Id == entry.FanGroupId))
            {
                continue;
            }

            fans.Add((entry.FanGroupId, delta));
        }

        return fans;
    }

    private static (string FirstId, string SecondId) Ordered(string first, string second) =>
        string.CompareOrdinal(first, second) <= 0 ? (first, second) : (second, first);

    private static string PairKey(string first, string second)
    {
        (string left, string right) = Ordered(first, second);
        return left + "\u001f" + right;
    }
}
