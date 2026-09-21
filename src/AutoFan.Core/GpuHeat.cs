namespace AutoFan.Core;

/// <summary>
/// Watch GPU Core rise must be large enough to map GPU cooling.
/// </summary>
public static class GpuHeat
{
    public static bool IsUseful(BaselineRun? baseline)
    {
        if (baseline is null)
        {
            return false;
        }

        foreach (BaselineMetric metric in baseline.Metrics)
        {
            if (metric.Name != BaselineMetricNames.GpuRiseCelsius
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
