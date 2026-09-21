namespace AutoFan.Core;

public static class ReferenceStability
{
    public const int HoldCount = 3;

    public static double ProductFloorCelsius => InfluenceMapBuilder.NoneBandCelsius;

    public static double MinimumDetectable(double rangeCelsius) =>
        Math.Max(ProductFloorCelsius, rangeCelsius * 2);

    public static SensorStabilityResult Assess(SensorKind kind, IReadOnlyList<double?> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);

        var values = new List<double>(holds.Count);
        foreach (double? hold in holds)
        {
            if (hold is double value)
            {
                values.Add(value);
            }
        }

        if (values.Count < HoldCount)
        {
            return new SensorStabilityResult(
                kind,
                SensorStability.Unavailable,
                RangeCelsius: null,
                ProductFloorCelsius,
                values);
        }

        double range = values.Max() - values.Min();
        double mde = MinimumDetectable(range);
        SensorStability state = range < ProductFloorCelsius
            ? SensorStability.Stable
            : IsOneWay(values)
                ? SensorStability.Drifting
                : SensorStability.Noisy;
        return new SensorStabilityResult(kind, state, range, mde, values);
    }

    public static ReferenceAssessment FromHolds(IReadOnlyList<HardwareSnapshot> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);

        var cpu = new List<double?>(holds.Count);
        var gpu = new List<double?>(holds.Count);
        foreach (HardwareSnapshot snapshot in holds)
        {
            cpu.Add(PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature));
            gpu.Add(PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature));
        }

        return new ReferenceAssessment(
            Assess(SensorKind.CpuTemperature, cpu),
            Assess(SensorKind.GpuTemperature, gpu));
    }

    public static IReadOnlyList<BaselineMetric> ToMetrics(IReadOnlyList<HardwareSnapshot> holds)
    {
        ArgumentNullException.ThrowIfNull(holds);
        return ToMetrics(FromHolds(holds));
    }

    public static IReadOnlyList<BaselineMetric> ToMetrics(ReferenceAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        return
        [
            HoldMetric(BaselineMetricNames.CpuReferenceHold1Celsius, assessment.Cpu, 0),
            HoldMetric(BaselineMetricNames.CpuReferenceHold2Celsius, assessment.Cpu, 1),
            HoldMetric(BaselineMetricNames.CpuReferenceHold3Celsius, assessment.Cpu, 2),
            HoldMetric(BaselineMetricNames.GpuReferenceHold1Celsius, assessment.Gpu, 0),
            HoldMetric(BaselineMetricNames.GpuReferenceHold2Celsius, assessment.Gpu, 1),
            HoldMetric(BaselineMetricNames.GpuReferenceHold3Celsius, assessment.Gpu, 2),
            RangeMetric(BaselineMetricNames.CpuReferenceRangeCelsius, assessment.Cpu),
            RangeMetric(BaselineMetricNames.GpuReferenceRangeCelsius, assessment.Gpu),
            DetectableMetric(BaselineMetricNames.CpuMinimumDetectableCelsius, assessment.Cpu),
            DetectableMetric(BaselineMetricNames.GpuMinimumDetectableCelsius, assessment.Gpu),
            StateMetric(BaselineMetricNames.CpuReferenceState, assessment.Cpu),
            StateMetric(BaselineMetricNames.GpuReferenceState, assessment.Gpu),
        ];
    }

    public static ReferenceAssessment? FromBaseline(BaselineRun? baseline)
    {
        if (baseline is null
            || ReadState(baseline, BaselineMetricNames.CpuReferenceState) is not SensorStability cpuState
            || ReadState(baseline, BaselineMetricNames.GpuReferenceState) is not SensorStability gpuState)
        {
            return null;
        }

        return new ReferenceAssessment(
            FromMetrics(
                SensorKind.CpuTemperature,
                cpuState,
                baseline,
                BaselineMetricNames.CpuReferenceRangeCelsius,
                BaselineMetricNames.CpuMinimumDetectableCelsius,
                BaselineMetricNames.CpuReferenceHold1Celsius,
                BaselineMetricNames.CpuReferenceHold2Celsius,
                BaselineMetricNames.CpuReferenceHold3Celsius),
            FromMetrics(
                SensorKind.GpuTemperature,
                gpuState,
                baseline,
                BaselineMetricNames.GpuReferenceRangeCelsius,
                BaselineMetricNames.GpuMinimumDetectableCelsius,
                BaselineMetricNames.GpuReferenceHold1Celsius,
                BaselineMetricNames.GpuReferenceHold2Celsius,
                BaselineMetricNames.GpuReferenceHold3Celsius));
    }

    public static IReadOnlyList<InfluenceEntry> WithoutUnusableTargets(
        IReadOnlyList<InfluenceEntry> entries,
        ReferenceAssessment? assessment)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (assessment is null)
        {
            return entries;
        }

        IReadOnlyList<InfluenceEntry> result = entries;
        if (!assessment.IsUsable(InfluenceTarget.Cpu))
        {
            result = InfluenceMapBuilder.WithoutTarget(
                result,
                InfluenceTarget.Cpu,
                assessment.SkipReason(InfluenceTarget.Cpu) ?? FanTestReasons.CpuNotStable);
        }

        if (!assessment.IsUsable(InfluenceTarget.Gpu))
        {
            result = InfluenceMapBuilder.WithoutTarget(
                result,
                InfluenceTarget.Gpu,
                assessment.SkipReason(InfluenceTarget.Gpu) ?? FanTestReasons.GpuNotStable);
        }

        return result;
    }

    private static bool IsOneWay(IReadOnlyList<double> values)
    {
        double span = values[^1] - values[0];
        if (Math.Abs(span) < ProductFloorCelsius)
        {
            return false;
        }

        int sign = Math.Sign(span);
        for (int index = 1; index < values.Count; index++)
        {
            double step = values[index] - values[index - 1];
            if (step == 0)
            {
                continue;
            }

            if (Math.Sign(step) != sign)
            {
                return false;
            }
        }

        return true;
    }

    private static SensorStabilityResult FromMetrics(
        SensorKind kind,
        SensorStability state,
        BaselineRun baseline,
        string rangeName,
        string mdeName,
        params string[] holdNames)
    {
        var holds = new List<double>(holdNames.Length);
        foreach (string name in holdNames)
        {
            if (ReadValue(baseline, name) is double hold)
            {
                holds.Add(hold);
            }
        }

        double mde = ReadValue(baseline, mdeName) ?? ProductFloorCelsius;
        return new SensorStabilityResult(kind, state, ReadValue(baseline, rangeName), mde, holds);
    }

    private static SensorStability? ReadState(BaselineRun baseline, string name)
    {
        if (ReadValue(baseline, name) is not double value
            || value < (int)SensorStability.Unavailable
            || value > (int)SensorStability.Noisy
            || Math.Abs(value - Math.Round(value)) > 0.01)
        {
            return null;
        }

        return (SensorStability)(int)Math.Round(value);
    }

    private static double? ReadValue(BaselineRun baseline, string name)
    {
        foreach (BaselineMetric metric in baseline.Metrics)
        {
            if (metric.Name == name && metric.Value is double value)
            {
                return value;
            }
        }

        return null;
    }

    private static BaselineMetric HoldMetric(string name, SensorStabilityResult result, int index)
    {
        double? value = index < result.Holds.Count ? result.Holds[index] : null;
        return new BaselineMetric(
            name,
            value,
            "°C",
            value is null ? MetricEvidence.Unknown : MetricEvidence.Measured);
    }

    private static BaselineMetric RangeMetric(string name, SensorStabilityResult result) =>
        result.RangeCelsius is double range
            ? new BaselineMetric(name, range, "°C", MetricEvidence.Measured)
            : new BaselineMetric(name, null, "°C", MetricEvidence.Unknown);

    private static BaselineMetric DetectableMetric(string name, SensorStabilityResult result) =>
        new(name, result.MinimumDetectableCelsius, "°C", MetricEvidence.Measured);

    private static BaselineMetric StateMetric(string name, SensorStabilityResult result) =>
        new(name, (int)result.State, string.Empty, MetricEvidence.Measured);
}
