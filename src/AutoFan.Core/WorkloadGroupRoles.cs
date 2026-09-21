namespace AutoFan.Core;

public static class WorkloadGroupRoles
{
    public static IReadOnlyDictionary<string, WorkloadGroupRole> FromInfluence(
        IReadOnlyList<InfluenceEntry> influence)
    {
        ArgumentNullException.ThrowIfNull(influence);

        var roles = new Dictionary<string, WorkloadGroupRole>(StringComparer.Ordinal);
        foreach (InfluenceEntry entry in influence)
        {
            if (string.IsNullOrWhiteSpace(entry.FanGroupId) || roles.ContainsKey(entry.FanGroupId))
            {
                continue;
            }

            roles[entry.FanGroupId] = RoleOf(influence, entry.FanGroupId);
        }

        return roles;
    }

    public static WorkloadGroupRole RoleOf(IReadOnlyList<InfluenceEntry> influence, string groupId)
    {
        ArgumentNullException.ThrowIfNull(influence);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        bool cpu = HasUseful(influence, groupId, InfluenceTarget.Cpu);
        bool gpu = HasUseful(influence, groupId, InfluenceTarget.Gpu);
        if (cpu && gpu)
        {
            return WorkloadGroupRole.Both;
        }

        if (cpu)
        {
            return WorkloadGroupRole.Cpu;
        }

        if (gpu)
        {
            return WorkloadGroupRole.Gpu;
        }

        return WorkloadGroupRole.None;
    }

    public static bool ShouldRaiseFromPower(WorkloadGroupRole role, WorkloadPattern pattern) =>
        pattern switch
        {
            WorkloadPattern.Mixed => true,
            WorkloadPattern.Gaming => role is WorkloadGroupRole.Gpu or WorkloadGroupRole.Both,
            WorkloadPattern.Render => role is WorkloadGroupRole.Cpu or WorkloadGroupRole.Both,
            _ => false,
        };

    private static bool HasUseful(
        IReadOnlyList<InfluenceEntry> influence,
        string groupId,
        InfluenceTarget target)
    {
        foreach (InfluenceEntry entry in influence)
        {
            if (!string.Equals(entry.FanGroupId, groupId, StringComparison.Ordinal)
                || entry.Target != target
                || entry.Evidence != MetricEvidence.Measured
                || entry.DeltaCelsius is not double delta)
            {
                continue;
            }

            if (entry.Effect is not null and not InfluenceEffect.None)
            {
                return true;
            }

            if (Math.Abs(delta) > InfluenceMapBuilder.NoneBandCelsius)
            {
                return true;
            }
        }

        return false;
    }
}
