namespace AutoFan.Core;

public static class ScreenOrderer
{
    public static IReadOnlyList<FanGroup> Order(
        IReadOnlyList<FanGroup> candidates,
        CasePrior prior)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(prior);

        var ranked = new List<(int Index, int Rank, FanGroup Group)>(candidates.Count);
        for (int index = 0; index < candidates.Count; index++)
        {
            FanGroup group = candidates[index];
            CaseZone zone = CaseZoneMatcher.FromName(group.Name);
            ranked.Add((index, Rank(zone, prior), group));
        }

        return ranked
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Index)
            .Select(item => item.Group)
            .ToArray();
    }

    private static int Rank(CaseZone zone, CasePrior prior)
    {
        InfluenceTarget? target = prior.TargetOf(zone);
        if (zone == CaseZone.Unknown || target is null)
        {
            return 100;
        }

        int band = target == InfluenceTarget.Gpu ? 0 : 10;
        return band + ZoneOrder(zone);
    }

    private static int ZoneOrder(CaseZone zone) =>
        zone switch
        {
            CaseZone.Front => 0,
            CaseZone.Bottom => 1,
            CaseZone.Rear => 2,
            CaseZone.Top => 3,
            CaseZone.CpuHeader => 4,
            _ => 5,
        };
}
