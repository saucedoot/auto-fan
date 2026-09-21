namespace AutoFan.Core;

public static class InfluenceOverlay
{
    public static FanTestRun Apply(FanTestRun? previous, FanTestRun targeted)
    {
        ArgumentNullException.ThrowIfNull(targeted);
        if (previous is null)
        {
            return targeted;
        }

        var replaced = new HashSet<string>(StringComparer.Ordinal);
        foreach (InfluenceEntry entry in targeted.Influence)
        {
            replaced.Add(entry.FanGroupId);
        }

        foreach (FanTestSample sample in targeted.Samples)
        {
            replaced.Add(sample.FanGroupId);
        }

        IReadOnlyList<FanTestSample> samples =
        [
            .. previous.Samples.Where(sample => !replaced.Contains(sample.FanGroupId)),
            .. targeted.Samples,
        ];
        IReadOnlyList<InfluenceEntry> influence = Merge(previous.Influence, targeted.Influence);
        IReadOnlyList<SkippedFanGroup> skipped =
        [
            .. previous.Skipped.Where(skip => !replaced.Contains(skip.FanGroupId)),
            .. targeted.Skipped,
        ];
        return targeted with
        {
            Id = Guid.NewGuid(),
            Samples = samples,
            Influence = influence,
            Skipped = skipped,
        };
    }

    public static IReadOnlyList<InfluenceEntry> Merge(
        IReadOnlyList<InfluenceEntry>? previous,
        IReadOnlyList<InfluenceEntry> targeted)
    {
        ArgumentNullException.ThrowIfNull(targeted);
        if (previous is null || previous.Count == 0)
        {
            return targeted;
        }

        var replaced = new HashSet<string>(
            targeted.Select(static entry => entry.FanGroupId),
            StringComparer.Ordinal);
        return
        [
            .. previous.Where(entry => !replaced.Contains(entry.FanGroupId)),
            .. targeted,
        ];
    }
}
