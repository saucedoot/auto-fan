namespace AutoFan.Core;

/// <summary>
/// Builds a draft temperature-to-duty record. It does not command fans.
/// The separation and safety margin are provisional.
/// </summary>
public static class CurveBuilder
{
    public const double SafetyMarginCelsius = 5;

    public const string NotEnoughDetail =
        "Not enough settled points. Fans stay on BIOS and the NVIDIA driver.";

    public const string DraftDetail =
        "Draft curve only. It is not applied.";

    public const string CheckDetail =
        "Draft curve. A measured point was changed to keep duty from falling, so it needs a check before it can be trusted.";

    public static CoolingProfile Build(
        IReadOnlyList<CurveSample> samples,
        ThermalAbortLimits limits,
        IReadOnlyDictionary<string, int>? minimumStableDuty = null,
        double? userCpuCeiling = null,
        double? userGpuCeiling = null,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var points = new List<CurvePoint>();
        bool changed = false;
        foreach (IGrouping<string, CurveSample> group in samples.GroupBy(sample => sample.GroupId))
        {
            foreach (CurveSensor sensor in new[] { CurveSensor.Cpu, CurveSensor.Gpu })
            {
                (IReadOnlyList<CurvePoint> built, bool corrected) = BuildSeries(
                    group.Key,
                    sensor,
                    group.Where(sample => sample.Sensor == sensor).ToArray(),
                    limits,
                    minimumStableDuty,
                    sensor == CurveSensor.Cpu ? userCpuCeiling : userGpuCeiling);
                if (built.Count == 0)
                {
                    continue;
                }

                points.AddRange(built);
                changed |= corrected;
            }
        }

        ProfileState state = changed ? ProfileState.EditedCheckRequired : ProfileState.Draft;
        string detail = points.Count == 0
            ? NotEnoughDetail
            : changed ? CheckDetail : DraftDetail;
        return new CoolingProfile(
            Guid.NewGuid(),
            createdAt ?? DateTimeOffset.UtcNow,
            state,
            detail,
            points);
    }

    private static (IReadOnlyList<CurvePoint> Points, bool Changed) BuildSeries(
        string groupId,
        CurveSensor sensor,
        IReadOnlyList<CurveSample> samples,
        ThermalAbortLimits limits,
        IReadOnlyDictionary<string, int>? minimumStableDuty,
        double? userCeiling)
    {
        List<CurveSample> settled = samples
            .Where(sample => sample.Settled && sample.DutyPercent is > 0 and <= 100)
            .OrderBy(sample => sample.TemperatureCelsius)
            .ToList();
        var kept = new List<CurvePoint>();
        foreach (CurveSample sample in settled)
        {
            if (kept.Count > 0
                && sample.TemperatureCelsius - kept[^1].TemperatureCelsius <= HoldAssessor.MaxWindowRangeCelsius)
            {
                if (sample.DutyPercent > kept[^1].DutyPercent)
                {
                    kept[^1] = Point(groupId, sensor, sample.TemperatureCelsius, sample.DutyPercent, CurvePointOrigin.SettledHold);
                }

                continue;
            }

            kept.Add(Point(groupId, sensor, sample.TemperatureCelsius, sample.DutyPercent, CurvePointOrigin.SettledHold));
        }

        if (kept.Count < 2)
        {
            return ([], false);
        }

        bool changed = false;
        int floor = 0;
        if (minimumStableDuty is not null
            && minimumStableDuty.TryGetValue(groupId, out int stable)
            && stable is > 0 and <= 100)
        {
            floor = stable;
        }

        if (floor > kept[0].DutyPercent)
        {
            kept[0] = kept[0] with
            {
                DutyPercent = floor,
                Evidence = MetricEvidence.Modeled,
                Origin = CurvePointOrigin.DutyFloor,
            };
            changed = true;
        }

        for (int index = 1; index < kept.Count; index++)
        {
            if (kept[index].DutyPercent >= kept[index - 1].DutyPercent)
            {
                continue;
            }

            kept[index] = kept[index] with
            {
                DutyPercent = kept[index - 1].DutyPercent,
                Evidence = MetricEvidence.Modeled,
                Origin = CurvePointOrigin.MonotoneCorrection,
            };
            changed = true;
        }

        double ceiling = sensor == CurveSensor.Gpu ? limits.GpuCelsius : limits.CpuCelsius;
        double safetyAt = ceiling - SafetyMarginCelsius;
        if (userCeiling is double requested && requested < safetyAt && requested > kept[^1].TemperatureCelsius)
        {
            safetyAt = Math.Min(requested, ceiling);
        }

        if (safetyAt > kept[^1].TemperatureCelsius + HoldAssessor.MaxWindowRangeCelsius)
        {
            kept.Add(Point(groupId, sensor, safetyAt, 100, CurvePointOrigin.SafetyExtension));
        }
        else if (kept[^1].DutyPercent < 100)
        {
            kept[^1] = kept[^1] with
            {
                DutyPercent = 100,
                Evidence = MetricEvidence.Modeled,
                Origin = CurvePointOrigin.SafetyExtension,
            };
            changed = true;
        }

        return (kept, changed);
    }

    private static CurvePoint Point(
        string groupId,
        CurveSensor sensor,
        double temperature,
        int duty,
        CurvePointOrigin origin) =>
        new(
            groupId,
            sensor,
            temperature,
            duty,
            origin == CurvePointOrigin.SettledHold ? MetricEvidence.Measured : MetricEvidence.Modeled,
            origin);
}
