namespace AutoFan.Core;

public static class FanSpeedCurveBuilder
{
    public const int HoldDutyJumpPercent = 10;

    public static IReadOnlyList<FanSpeedCurve> Build(IReadOnlyList<FanTestSample>? samples)
    {
        if (samples is null || samples.Count == 0)
        {
            return [];
        }

        var curves = new List<FanSpeedCurve>();
        foreach (string groupId in DistinctGroupIds(samples))
        {
            IReadOnlyList<FanTestSample> groupSamples = samples
                .Where(sample => sample.FanGroupId == groupId)
                .ToArray();
            string groupName = groupSamples[0].FanGroupName;
            foreach (InfluenceTarget target in Enum.GetValues<InfluenceTarget>())
            {
                IReadOnlyList<FanSpeedPoint> points = Points(groupSamples, groupId, target);
                if (points.Count == 0)
                {
                    continue;
                }

                curves.Add(new FanSpeedCurve(
                    groupId,
                    groupName,
                    target,
                    points,
                    MetricEvidence.Measured));
            }
        }

        return curves;
    }

    private static IReadOnlyList<FanSpeedPoint> Points(
        IReadOnlyList<FanTestSample> samples,
        string groupId,
        InfluenceTarget target)
    {
        var points = new List<FanSpeedPoint>();
        var seenRpm = new HashSet<int>();
        foreach (IReadOnlyList<FanTestSample> hold in Holds(samples, groupId))
        {
            if (!hold.All(static sample => sample.Settled))
            {
                continue;
            }

            IReadOnlyList<HardwareSnapshot> snapshots = hold
                .Select(static sample => sample.Snapshot)
                .ToArray();
            if (!TemperatureSettle.RelevantTempsSettled(snapshots))
            {
                continue;
            }

            FanGroup? fan = LastFan(hold, groupId);
            if (fan?.Rpm is not > 0)
            {
                continue;
            }

            IReadOnlyList<double> temps = TailTemps(hold, target);
            if (temps.Count == 0)
            {
                continue;
            }

            int rpmKey = (int)Math.Round(fan.Rpm.Value, MidpointRounding.AwayFromZero);
            if (!seenRpm.Add(rpmKey))
            {
                continue;
            }

            points.Add(new FanSpeedPoint(fan.Rpm.Value, temps.Average()));
        }

        points.Sort(static (left, right) => left.Rpm.CompareTo(right.Rpm));
        return points;
    }

    private static IReadOnlyList<IReadOnlyList<FanTestSample>> Holds(
        IReadOnlyList<FanTestSample> samples,
        string groupId)
    {
        var holds = new List<List<FanTestSample>>();
        List<FanTestSample>? current = null;
        foreach (FanTestSample sample in samples)
        {
            if (!InfluenceMapBuilder.IsSpeedStage(sample.Stage) || !sample.Settled)
            {
                continue;
            }

            if (current is null || current.Count == 0)
            {
                current = [sample];
                continue;
            }

            FanTestSample last = current[^1];
            if (sample.Stage != last.Stage || DutyJumped(last, sample, groupId))
            {
                holds.Add(current);
                current = [sample];
                continue;
            }

            current.Add(sample);
        }

        if (current is { Count: > 0 })
        {
            holds.Add(current);
        }

        return holds;
    }

    private static bool DutyJumped(FanTestSample last, FanTestSample next, string groupId)
    {
        int? previous = DutyOf(last, groupId);
        int? current = DutyOf(next, groupId);
        return previous is int left
            && current is int right
            && Math.Abs(right - left) >= HoldDutyJumpPercent;
    }

    private static int? DutyOf(FanTestSample sample, string groupId) =>
        sample.Snapshot.FanGroups.FirstOrDefault(group => group.Id == groupId)?.DutyCyclePercent;

    private static IReadOnlyList<double> TailTemps(IReadOnlyList<FanTestSample> samples, InfluenceTarget target)
    {
        var values = new List<double>();
        foreach (FanTestSample sample in samples)
        {
            if (PreferredTemperature.Read(sample.Snapshot, target) is double value)
            {
                values.Add(value);
            }
        }

        if (values.Count <= ThermalDynamics.SettleWindowSamples)
        {
            return values;
        }

        return values.Skip(values.Count - ThermalDynamics.SettleWindowSamples).ToArray();
    }

    private static FanGroup? LastFan(IReadOnlyList<FanTestSample> samples, string groupId)
    {
        for (int index = samples.Count - 1; index >= 0; index--)
        {
            FanGroup? fan = samples[index].Snapshot.FanGroups.FirstOrDefault(group => group.Id == groupId);
            if (fan is not null)
            {
                return fan;
            }
        }

        return null;
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
}
