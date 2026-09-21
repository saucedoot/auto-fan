namespace AutoFan.Core;

public static class PolicyOptimizer
{
    public const string NeedFanTestsReason = "Need finished fan tests first.";

    public static CoolingPolicy? Recommend(
        ThermalModel model,
        DiminishingReturnsReport returns,
        CoolingPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(returns);
        ArgumentNullException.ThrowIfNull(preferences);

        if (model.Confidence == ModelConfidence.None
            || model.FanTestStatus is null
            || (model.FanTestStatus != FanTestRunStatus.Completed
                && model.FanTestStatus != FanTestRunStatus.Cancelled
                && model.FanTestStatus != FanTestRunStatus.Aborted))
        {
            return null;
        }

        bool conservative = model.FanTestStatus != FanTestRunStatus.Completed;
        if (conservative && model.MeasuredGroupIds().Count == 0)
        {
            return null;
        }

        double blend = conservative ? Math.Min(preferences.Blend, 0.25) : preferences.Blend;
        var groups = new List<GroupPolicy>();
        foreach (string groupId in model.MeasuredGroupIds())
        {
            if (TryBuildGroup(model, returns, blend, groupId) is GroupPolicy group)
            {
                groups.Add(group);
            }
        }

        if (groups.Count == 0)
        {
            return null;
        }

        return new CoolingPolicy(
            groups,
            blend,
            preferences.CpuTarget,
            preferences.GpuTarget,
            Summarize(groups, blend),
            model.TestCpuCelsius,
            model.TestGpuCelsius);
    }

    public static string UnavailableReason(ThermalModel? model)
    {
        if (model is not null
            && model.FanTestStatus is FanTestRunStatus.Completed
                or FanTestRunStatus.Cancelled
                or FanTestRunStatus.Aborted
            && model.Confidence != ModelConfidence.None
            && model.MeasuredGroupIds().Count > 0)
        {
            return "Need a fan that actually changed a temperature.";
        }

        return NeedFanTestsReason;
    }

    public static int DutyForRpm(
        int dutyBefore,
        int dutyAfter,
        double rpmBefore,
        double rpmAfter,
        double rpm)
    {
        if (rpmAfter == rpmBefore)
        {
            return ClampDuty(dutyBefore);
        }

        double duty = dutyBefore + ((rpm - rpmBefore) * (dutyAfter - dutyBefore) / (rpmAfter - rpmBefore));
        return ClampDuty((int)Math.Round(duty, MidpointRounding.AwayFromZero));
    }

    public static int BlendDuty(int quietDuty, int coolDuty, double blend)
    {
        double t = Math.Clamp(blend, 0, 1);
        return ClampDuty((int)Math.Round(quietDuty + (t * (coolDuty - quietDuty)), MidpointRounding.AwayFromZero));
    }

    private static GroupPolicy? TryBuildGroup(
        ThermalModel model,
        DiminishingReturnsReport returns,
        double blend,
        string groupId)
    {
        InfluenceEntry? map = DutyMap(model, groupId);
        if (map is null || map.DutyBefore is not int dutyBefore)
        {
            return null;
        }

        DiminishingReturnsBand? band = BestBand(returns, groupId);
        bool hasUsefulEffect = HasUsefulEffect(model, groupId);
        if (!hasUsefulEffect && band?.RecommendedRpm is null)
        {
            return null;
        }

        int quietDuty = dutyBefore;
        int coolDuty = map.DutyAfter
            ?? Math.Min(SafetyLimits.MaxDutyPercent, dutyBefore + FanTestSchedule.DutyStepPercent);
        double? appliedRpm = null;

        if (band?.RecommendedRpm is double recommended
            && map.DutyAfter is int dutyAfter
            && map.RpmBefore is double rpmBefore
            && map.RpmAfter is double rpmAfter)
        {
            quietDuty = DutyForRpm(dutyBefore, dutyAfter, rpmBefore, rpmAfter, recommended);
            double coolRpm = band.UsefulRpmMax ?? recommended;
            coolDuty = DutyForRpm(dutyBefore, dutyAfter, rpmBefore, rpmAfter, coolRpm);
            appliedRpm = recommended + (blend * (coolRpm - recommended));
        }

        if (coolDuty < quietDuty)
        {
            (quietDuty, coolDuty) = (coolDuty, quietDuty);
        }

        int duty = BlendDuty(quietDuty, coolDuty, blend);
        return new GroupPolicy(
            groupId,
            model.GroupName(groupId),
            quietDuty,
            coolDuty,
            duty,
            appliedRpm);
    }

    private static InfluenceEntry? DutyMap(ThermalModel model, string groupId)
    {
        foreach (InfluenceEntry entry in model.Influence)
        {
            if (string.Equals(entry.FanGroupId, groupId, StringComparison.Ordinal)
                && entry.Evidence == MetricEvidence.Measured
                && entry.DutyBefore is not null)
            {
                return entry;
            }
        }

        return null;
    }

    private static bool HasUsefulEffect(ThermalModel model, string groupId)
    {
        foreach (InfluenceEntry entry in model.Influence)
        {
            if (!string.Equals(entry.FanGroupId, groupId, StringComparison.Ordinal)
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

    private static DiminishingReturnsBand? BestBand(DiminishingReturnsReport returns, string groupId)
    {
        DiminishingReturnsBand? fallback = null;
        foreach (DiminishingReturnsBand band in returns.Bands)
        {
            if (!string.Equals(band.FanGroupId, groupId, StringComparison.Ordinal)
                || band.RecommendedRpm is null)
            {
                continue;
            }

            if (band.Target == InfluenceTarget.Cpu)
            {
                return band;
            }

            fallback ??= band;
            if (band.Target == InfluenceTarget.Gpu && fallback.Target != InfluenceTarget.Gpu)
            {
                fallback = band;
            }
        }

        return fallback;
    }

    private static string Summarize(IReadOnlyList<GroupPolicy> groups, double blend)
    {
        string lean = blend <= 0.25
            ? "quieter"
            : blend >= 0.75
                ? "cooler"
                : "balanced";
        return $"A {lean} setting for {groups.Count} motherboard fan {(groups.Count == 1 ? "group" : "groups")}.";
    }

    private static int ClampDuty(int duty) =>
        Math.Clamp(duty, SafetyLimits.MinDutyPercent, SafetyLimits.MaxDutyPercent);
}
