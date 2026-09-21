namespace AutoFan.Core;

public static class ThermalDynamics
{
    public const double SettleBandCelsius = 0.5;
    public const int SettleWindowSamples = 5;

    public static IReadOnlyList<BaselineMetric> Summarize(IReadOnlyList<BaselineSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        return
        [
            Ambient(samples),
            TemperatureChange(
                samples,
                SensorKind.CpuTemperature,
                BaselineMetricNames.CpuEverydayRiseCelsius,
                rise: true,
                BaselinePhase.Everyday),
            TemperatureChange(
                samples,
                SensorKind.GpuTemperature,
                BaselineMetricNames.GpuEverydayRiseCelsius,
                rise: true,
                BaselinePhase.Everyday),
            TemperatureChange(
                samples,
                SensorKind.CpuTemperature,
                BaselineMetricNames.CpuRiseCelsius,
                rise: true,
                StrongestHeat(samples)),
            TemperatureChange(
                samples,
                SensorKind.GpuTemperature,
                BaselineMetricNames.GpuRiseCelsius,
                rise: true,
                StrongestHeat(samples)),
            TemperatureChange(
                samples,
                SensorKind.CpuTemperature,
                BaselineMetricNames.CpuDecayCelsius,
                rise: false,
                StrongestHeat(samples)),
            TemperatureChange(
                samples,
                SensorKind.GpuTemperature,
                BaselineMetricNames.GpuDecayCelsius,
                rise: false,
                StrongestHeat(samples)),
            Settle(samples, SensorKind.CpuTemperature, BaselineMetricNames.CpuSettleSeconds),
            Settle(samples, SensorKind.GpuTemperature, BaselineMetricNames.GpuSettleSeconds),
        ];
    }

    private static BaselinePhase StrongestHeat(IReadOnlyList<BaselineSample> samples)
    {
        foreach (BaselineSample sample in samples)
        {
            if (sample.Phase == BaselinePhase.Low)
            {
                return BaselinePhase.Low;
            }
        }

        return BaselinePhase.High;
    }

    public static double? FirstAmbient(IReadOnlyList<BaselineSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        foreach (BaselineSample sample in samples)
        {
            if (ReadAmbient(sample.Snapshot) is double ambient)
            {
                return ambient;
            }
        }

        return null;
    }

    public static double? ReadAmbient(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (SensorReading sensor in snapshot.Sensors)
        {
            if (sensor.Kind == SensorKind.AmbientTemperature && sensor.Value is double ambient)
            {
                return ambient;
            }
        }

        return null;
    }

    private static BaselineMetric Ambient(IReadOnlyList<BaselineSample> samples)
    {
        double? ambient = FirstAmbient(samples);
        return ambient is double value
            ? new BaselineMetric(BaselineMetricNames.AmbientCelsius, value, "°C", MetricEvidence.Measured)
            : Unknown(BaselineMetricNames.AmbientCelsius, "°C");
    }

    private static BaselineMetric TemperatureChange(
        IReadOnlyList<BaselineSample> samples,
        SensorKind kind,
        string name,
        bool rise,
        BaselinePhase heatPhase)
    {
        IReadOnlyList<TimedValue> idle = Values(samples, BaselinePhase.Idle, kind);
        IReadOnlyList<TimedValue> heat = Values(samples, heatPhase, kind);
        IReadOnlyList<TimedValue> cooldown = Values(samples, BaselinePhase.Cooldown, kind);
        if (idle.Count == 0 || heat.Count == 0)
        {
            return Unknown(name, "°C");
        }

        double idleMean = Mean(idle);
        double heatPeak = heat.Max(static point => point.Value);
        if (rise)
        {
            return new BaselineMetric(name, heatPeak - idleMean, "°C", MetricEvidence.Measured);
        }

        if (cooldown.Count == 0)
        {
            return Unknown(name, "°C");
        }

        double settled = Mean(Tail(cooldown, SettleWindowSamples));
        return new BaselineMetric(name, heatPeak - settled, "°C", MetricEvidence.Measured);
    }

    private static BaselineMetric Settle(
        IReadOnlyList<BaselineSample> samples,
        SensorKind kind,
        string name)
    {
        IReadOnlyList<TimedValue> cooldown = Values(samples, BaselinePhase.Cooldown, kind);
        if (cooldown.Count < SettleWindowSamples)
        {
            return Unknown(name, "s");
        }

        DateTimeOffset start = cooldown[0].At;
        for (int index = 0; index <= cooldown.Count - SettleWindowSamples; index++)
        {
            IReadOnlyList<TimedValue> window = cooldown.Skip(index).Take(SettleWindowSamples).ToArray();
            double min = window.Min(static point => point.Value);
            double max = window.Max(static point => point.Value);
            if (max - min <= SettleBandCelsius * 2)
            {
                double seconds = (window[^1].At - start).TotalSeconds;
                return new BaselineMetric(name, seconds, "s", MetricEvidence.Measured);
            }
        }

        return Unknown(name, "s");
    }

    private static IReadOnlyList<TimedValue> Values(
        IReadOnlyList<BaselineSample> samples,
        BaselinePhase phase,
        SensorKind kind)
    {
        var values = new List<TimedValue>();
        foreach (BaselineSample sample in samples)
        {
            if (sample.Phase != phase)
            {
                continue;
            }

            if (PreferredTemperature.Read(sample.Snapshot, kind) is double value)
            {
                values.Add(new TimedValue(sample.CapturedAt, value));
            }
        }

        return values;
    }

    private static IReadOnlyList<TimedValue> Tail(IReadOnlyList<TimedValue> values, int count)
    {
        if (values.Count <= count)
        {
            return values;
        }

        return values.Skip(values.Count - count).ToArray();
    }

    private static double Mean(IReadOnlyList<TimedValue> values) =>
        values.Average(static point => point.Value);

    private static BaselineMetric Unknown(string name, string unit) =>
        new(name, null, unit, MetricEvidence.Unknown);

    private readonly record struct TimedValue(DateTimeOffset At, double Value);
}
