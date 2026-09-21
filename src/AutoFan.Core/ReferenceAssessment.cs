namespace AutoFan.Core;

public sealed record ReferenceAssessment(
    SensorStabilityResult Cpu,
    SensorStabilityResult Gpu)
{
    public bool IsUsable(InfluenceTarget target) =>
        target switch
        {
            InfluenceTarget.Cpu => Cpu.State == SensorStability.Stable,
            InfluenceTarget.Gpu => Gpu.State == SensorStability.Stable,
            _ => true,
        };

    public double NoneBand(InfluenceTarget target) =>
        target switch
        {
            InfluenceTarget.Cpu => Cpu.MinimumDetectableCelsius,
            InfluenceTarget.Gpu => Gpu.MinimumDetectableCelsius,
            _ => InfluenceMapBuilder.NoneBandCelsius,
        };

    public string? SkipReason(InfluenceTarget target)
    {
        SensorStabilityResult? result = target switch
        {
            InfluenceTarget.Cpu => Cpu,
            InfluenceTarget.Gpu => Gpu,
            _ => null,
        };
        if (result is null || result.State == SensorStability.Stable)
        {
            return null;
        }

        return target == InfluenceTarget.Cpu
            ? FanTestReasons.CpuNotStable
            : FanTestReasons.GpuNotStable;
    }

    public string WatchNote
    {
        get
        {
            if (Cpu.State == SensorStability.Stable && Gpu.State == SensorStability.Stable)
            {
                return "CPU and GPU temperatures were steady.";
            }

            return $"{Describe(Gpu, "GPU")} {Describe(Cpu, "CPU")}".Trim();
        }
    }

    private static string Describe(SensorStabilityResult result, string name) =>
        result.State switch
        {
            SensorStability.Stable => $"{name} temperatures were steady.",
            SensorStability.Drifting => $"{name} wandered, so {name} cooling stays unproven.",
            SensorStability.Noisy => $"{name} temperatures jumped around, so {name} cooling stays unproven.",
            _ => $"{name} temperature was missing.",
        };
}
