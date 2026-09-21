namespace AutoFan.Core;

public sealed record PowerReference(
    double? IdleCpuWatts,
    double? IdleGpuWatts,
    double? EverydayCpuWatts,
    double? EverydayGpuWatts,
    double? TestCpuWatts,
    double? TestGpuWatts)
{
    public static PowerReference Empty { get; } = new(null, null, null, null, null, null);

    public static PowerReference FromBaseline(BaselineRun? baseline)
    {
        if (baseline is null)
        {
            return Empty;
        }

        return new(
            Mean(baseline, BaselinePhase.Idle, SensorKind.CpuPower),
            Mean(baseline, BaselinePhase.Idle, SensorKind.GpuPower),
            Mean(baseline, BaselinePhase.Everyday, SensorKind.CpuPower),
            Mean(baseline, BaselinePhase.Everyday, SensorKind.GpuPower),
            Mean(baseline, BaselinePhase.Low, SensorKind.CpuPower),
            Mean(baseline, BaselinePhase.Low, SensorKind.GpuPower));
    }

    public bool HasAnyWatts =>
        IdleCpuWatts is not null
        || IdleGpuWatts is not null
        || EverydayCpuWatts is not null
        || EverydayGpuWatts is not null
        || TestCpuWatts is not null
        || TestGpuWatts is not null;

    private static double? Mean(BaselineRun baseline, BaselinePhase phase, SensorKind kind)
    {
        var values = new List<double>();
        foreach (BaselineSample sample in baseline.Samples)
        {
            if (sample.Phase != phase)
            {
                continue;
            }

            if (PreferredPower.Read(sample.Snapshot, kind) is double watts)
            {
                values.Add(watts);
            }
        }

        return values.Count == 0 ? null : values.Average();
    }
}
