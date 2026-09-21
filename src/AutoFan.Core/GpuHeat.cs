namespace AutoFan.Core;

/// <summary>
/// Watch GPU Core rise must be large enough to map GPU cooling.
/// </summary>
public static class GpuHeat
{
    public static bool IsUseful(BaselineRun? baseline) =>
        RiseIsUseful(baseline, BaselineMetricNames.GpuRiseCelsius);

    public static bool EverydayIsUseful(BaselineRun? baseline) =>
        RiseIsUseful(baseline, BaselineMetricNames.GpuEverydayRiseCelsius);

    private static bool RiseIsUseful(BaselineRun? baseline, string metricName)
    {
        if (baseline is null)
        {
            return false;
        }

        foreach (BaselineMetric metric in baseline.Metrics)
        {
            if (metric.Name != metricName
                || metric.Evidence != MetricEvidence.Measured
                || metric.Value is not double rise)
            {
                continue;
            }

            return rise >= HeatCalibrator.TargetRiseCelsius;
        }

        return false;
    }
}
