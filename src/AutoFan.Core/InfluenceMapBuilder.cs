namespace AutoFan.Core;

public static class InfluenceMapBuilder
{
    public const double NoneBandCelsius = 0.5;
    public const double LowBandCelsius = 1.5;
    public const double MediumBandCelsius = 3.0;
    public const double HighBandCelsius = 5.0;

    public static IReadOnlyList<InfluenceEntry> Build(IReadOnlyList<FanTestSample> samples) =>
        Build(samples, stability: null);

    public static IReadOnlyList<InfluenceEntry> Build(
        IReadOnlyList<FanTestSample> samples,
        ReferenceAssessment? stability)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var entries = new List<InfluenceEntry>();
        foreach (string groupId in DistinctGroupIds(samples))
        {
            IReadOnlyList<FanTestSample> groupSamples = samples
                .Where(sample => sample.FanGroupId == groupId)
                .ToArray();
            string groupName = groupSamples[0].FanGroupName;
            IReadOnlyList<FanTestSample> reference = groupSamples
                .Where(sample => sample.Stage == FanTestStage.Reference)
                .ToArray();
            IReadOnlyList<FanTestSample> speeds = groupSamples
                .Where(sample => IsSpeedStage(sample.Stage) && !IsStalled(sample, groupId))
                .ToArray();
            if (reference.Count == 0 || speeds.Count == 0)
            {
                continue;
            }

            FanGroup? before = FirstGroup(reference, groupId);
            foreach (InfluenceTarget target in Enum.GetValues<InfluenceTarget>())
            {
                entries.Add(BuildEntry(
                    groupId,
                    groupName,
                    target,
                    reference,
                    speeds,
                    before,
                    stability));
            }
        }

        return entries;
    }

    public static IReadOnlyList<InfluenceEntry> UnknownForGroup(
        string fanGroupId,
        string fanGroupName,
        string reason,
        int? dutyBefore = null,
        double? rpmBefore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return Enum.GetValues<InfluenceTarget>()
            .Select(target => new InfluenceEntry(
                fanGroupId,
                fanGroupName,
                target,
                DeltaCelsius: null,
                Effect: null,
                MetricEvidence.Unknown,
                dutyBefore,
                DutyAfter: null,
                rpmBefore,
                RpmAfter: null,
                reason))
            .ToArray();
    }

    public static InfluenceEffect Classify(double deltaCelsius) =>
        Classify(deltaCelsius, NoneBandCelsius);

    public static InfluenceEffect Classify(double deltaCelsius, double noneBandCelsius)
    {
        double band = noneBandCelsius > 0 ? noneBandCelsius : NoneBandCelsius;
        double magnitude = Math.Abs(deltaCelsius);
        if (magnitude < band)
        {
            return InfluenceEffect.None;
        }

        if (magnitude < LowBandCelsius)
        {
            return InfluenceEffect.Low;
        }

        if (magnitude < MediumBandCelsius)
        {
            return InfluenceEffect.Medium;
        }

        if (magnitude < HighBandCelsius)
        {
            return InfluenceEffect.High;
        }

        return InfluenceEffect.VeryHigh;
    }

    public static bool IsSpeedStage(FanTestStage stage) =>
        stage is FanTestStage.Perturb or FanTestStage.Screen or FanTestStage.Refine;

    public static bool MovedATemperature(IReadOnlyList<InfluenceEntry> influence, string fanGroupId)
    {
        ArgumentNullException.ThrowIfNull(influence);
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);

        foreach (InfluenceEntry entry in influence)
        {
            if (!string.Equals(entry.FanGroupId, fanGroupId, StringComparison.Ordinal)
                || entry.Evidence != MetricEvidence.Measured
                || entry.DeltaCelsius is null)
            {
                continue;
            }

            if (entry.Effect is not null and not InfluenceEffect.None)
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> GroupsThatMoved(
        IReadOnlyList<InfluenceEntry> influence,
        InfluenceTarget target)
    {
        ArgumentNullException.ThrowIfNull(influence);

        var ids = new List<string>();
        foreach (InfluenceEntry entry in influence)
        {
            if (entry.Target != target
                || entry.Evidence != MetricEvidence.Measured
                || entry.DeltaCelsius is null
                || ids.Contains(entry.FanGroupId, StringComparer.Ordinal))
            {
                continue;
            }

            if (entry.Effect is not null and not InfluenceEffect.None)
            {
                ids.Add(entry.FanGroupId);
            }
        }

        return ids;
    }

    private static InfluenceEntry BuildEntry(
        string groupId,
        string groupName,
        InfluenceTarget target,
        IReadOnlyList<FanTestSample> reference,
        IReadOnlyList<FanTestSample> speeds,
        FanGroup? before,
        ReferenceAssessment? stability)
    {
        SensorKind kind = KindOf(target);
        IReadOnlyList<double> referenceTemps = Temperatures(reference, kind);
        IReadOnlyList<FanTestSample>? bestSpeeds = null;
        double? bestDelta = null;
        FanGroup? after = null;
        foreach (IReadOnlyList<FanTestSample> bucket in GroupByDuty(speeds, groupId))
        {
            IReadOnlyList<double> speedTemps = Tail(Temperatures(bucket, kind), ThermalDynamics.SettleWindowSamples);
            if (referenceTemps.Count == 0 || speedTemps.Count == 0)
            {
                continue;
            }

            double delta = referenceTemps.Average() - speedTemps.Average();
            FanGroup? candidate = LastGroup(bucket, groupId);
            if (!SpedUp(before, candidate))
            {
                continue;
            }

            if (bestDelta is null || Math.Abs(delta) > Math.Abs(bestDelta.Value))
            {
                bestDelta = delta;
                bestSpeeds = bucket;
                after = candidate;
            }
        }

        if (bestDelta is null || bestSpeeds is null)
        {
            return new InfluenceEntry(
                groupId,
                groupName,
                target,
                DeltaCelsius: null,
                Effect: null,
                MetricEvidence.Unknown,
                before?.DutyCyclePercent,
                DutyAfter: null,
                before?.Rpm,
                RpmAfter: null,
                FanTestReasons.NoSpeedUp);
        }

        return new InfluenceEntry(
            groupId,
            groupName,
            target,
            bestDelta.Value,
            Classify(bestDelta.Value, stability?.NoneBand(target) ?? NoneBandCelsius),
            MetricEvidence.Measured,
            before?.DutyCyclePercent,
            after?.DutyCyclePercent,
            before?.Rpm,
            after?.Rpm);
    }

    public static IReadOnlyList<InfluenceEntry> WithoutGpuTargets(
        IReadOnlyList<InfluenceEntry> entries,
        string reason) =>
        WithoutTarget(entries, InfluenceTarget.Gpu, reason);

    public static IReadOnlyList<InfluenceEntry> WithoutTarget(
        IReadOnlyList<InfluenceEntry> entries,
        InfluenceTarget target,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var result = new List<InfluenceEntry>(entries.Count);
        foreach (InfluenceEntry entry in entries)
        {
            if (entry.Target != target)
            {
                result.Add(entry);
                continue;
            }

            result.Add(entry with
            {
                DeltaCelsius = null,
                Effect = null,
                Evidence = MetricEvidence.Unknown,
                DutyAfter = null,
                RpmAfter = null,
                SkipReason = reason,
            });
        }

        return result;
    }

    private static bool SpedUp(FanGroup? before, FanGroup? after)
    {
        if (after is null)
        {
            return false;
        }

        if (before?.Rpm is double rpmBefore && after.Rpm is double rpmAfter && rpmAfter <= rpmBefore)
        {
            return false;
        }

        int dutyBefore = before?.DutyCyclePercent ?? 0;
        int dutyAfter = after.DutyCyclePercent ?? 0;
        return dutyAfter - dutyBefore >= PolicySession.MaxDutyStepPercent;
    }

    private static IReadOnlyList<IReadOnlyList<FanTestSample>> GroupByDuty(
        IReadOnlyList<FanTestSample> speeds,
        string groupId)
    {
        var buckets = new List<(int? Duty, List<FanTestSample> Samples)>();
        foreach (FanTestSample sample in speeds)
        {
            int? duty = FirstGroup([sample], groupId)?.DutyCyclePercent;
            (int? Duty, List<FanTestSample> Samples)? match = null;
            foreach ((int? Duty, List<FanTestSample> Samples) bucket in buckets)
            {
                if (bucket.Duty == duty)
                {
                    match = bucket;
                    break;
                }
            }

            if (match is { } found)
            {
                found.Samples.Add(sample);
            }
            else
            {
                buckets.Add((duty, [sample]));
            }
        }

        return buckets.Select(static bucket => (IReadOnlyList<FanTestSample>)bucket.Samples).ToArray();
    }

    private static bool IsStalled(FanTestSample sample, string groupId)
    {
        FanGroup? group = sample.Snapshot.FanGroups.FirstOrDefault(fan => fan.Id == groupId);
        return group is null || group.Rpm is not > 0;
    }

    private static IReadOnlyList<string> DistinctGroupIds(IReadOnlyList<FanTestSample> samples)
    {
        var ids = new List<string>();
        foreach (FanTestSample sample in samples)
        {
            if (!ids.Contains(sample.FanGroupId, StringComparer.Ordinal))
            {
                ids.Add(sample.FanGroupId);
            }
        }

        return ids;
    }

    private static FanGroup? FirstGroup(IReadOnlyList<FanTestSample> samples, string groupId)
    {
        foreach (FanTestSample sample in samples)
        {
            FanGroup? group = sample.Snapshot.FanGroups.FirstOrDefault(fan => fan.Id == groupId);
            if (group is not null)
            {
                return group;
            }
        }

        return null;
    }

    private static FanGroup? LastGroup(IReadOnlyList<FanTestSample> samples, string groupId)
    {
        for (int index = samples.Count - 1; index >= 0; index--)
        {
            FanGroup? group = samples[index].Snapshot.FanGroups.FirstOrDefault(fan => fan.Id == groupId);
            if (group is not null)
            {
                return group;
            }
        }

        return null;
    }

    private static IReadOnlyList<double> Temperatures(IReadOnlyList<FanTestSample> samples, SensorKind kind)
    {
        var values = new List<double>();
        foreach (FanTestSample sample in samples)
        {
            if (PreferredTemperature.Read(sample.Snapshot, kind) is double value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static IReadOnlyList<double> Tail(IReadOnlyList<double> values, int count)
    {
        if (values.Count <= count)
        {
            return values;
        }

        return values.Skip(values.Count - count).ToArray();
    }

    private static SensorKind KindOf(InfluenceTarget target) =>
        target switch
        {
            InfluenceTarget.Cpu => SensorKind.CpuTemperature,
            InfluenceTarget.Gpu => SensorKind.GpuTemperature,
            InfluenceTarget.Vrm => SensorKind.VrmTemperature,
            InfluenceTarget.Case => SensorKind.CaseTemperature,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, message: null),
        };
}
