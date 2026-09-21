using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class ThermalAbortLimitsTests
{
    [Fact]
    public void FromUser_cannot_raise_the_floor()
    {
        ThermalAbortLimits limits = ThermalAbortLimits.FromUser(95, 90);

        Assert.Equal(SafetyLimits.CpuAbortCelsius, limits.CpuCelsius);
        Assert.Equal(SafetyLimits.GpuAbortCelsius, limits.GpuCelsius);
    }

    [Fact]
    public void FromUser_clamps_below_the_start_gate_up_to_65()
    {
        ThermalAbortLimits limits = ThermalAbortLimits.FromUser(40, 40);

        Assert.Equal(ThermalAbortLimits.MinAbortCelsius, limits.CpuCelsius);
        Assert.Equal(ThermalAbortLimits.MinAbortCelsius, limits.GpuCelsius);
    }

    [Fact]
    public void Missing_values_keep_the_floor()
    {
        ThermalAbortLimits limits = ThermalAbortLimits.FromUser(null, null);

        Assert.Equal(SafetyLimits.CpuAbortCelsius, limits.CpuCelsius);
        Assert.Equal(SafetyLimits.GpuAbortCelsius, limits.GpuCelsius);
    }

    [Fact]
    public void Evaluate_uses_a_tighter_gpu_abort()
    {
        ThermalAbortLimits limits = ThermalAbortLimits.FromUser(90, 75);
        HardwareSnapshot atLimit = Snapshot(cpu: 80, gpu: 75);
        HardwareSnapshot justUnder = Snapshot(cpu: 80, gpu: 74);

        Assert.Equal(ThermalAbortReason.GpuOverLimit, SafetyLimits.Evaluate(atLimit, limits));
        Assert.Null(SafetyLimits.Evaluate(justUnder, limits));
        Assert.Contains("75", SafetyLimits.Describe(ThermalAbortReason.GpuOverLimit, limits), StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_without_limits_still_uses_the_floor()
    {
        HardwareSnapshot snapshot = Snapshot(cpu: 89, gpu: 82);

        Assert.Null(SafetyLimits.Evaluate(snapshot));
        Assert.Equal(ThermalAbortReason.CpuOverLimit, SafetyLimits.Evaluate(Snapshot(cpu: 90, gpu: 50)));
        Assert.Equal(ThermalAbortReason.GpuOverLimit, SafetyLimits.Evaluate(Snapshot(cpu: 50, gpu: 83)));
    }

    [Fact]
    public void Cooling_targets_stay_below_the_chosen_abort()
    {
        var preferences = new CoolingPreferences(0.5, 80, 80, 90, 75);

        Assert.Equal(80, preferences.CpuTarget);
        Assert.Equal(74, preferences.GpuTarget);
        Assert.Equal(75, preferences.GpuAbort);
        Assert.Equal(89, CoolingPreferences.ClampCpu(95, 90));
        Assert.Equal(74, CoolingPreferences.ClampGpu(90, 75));
    }

    private static HardwareSnapshot Snapshot(double cpu, double gpu)
    {
        return new HardwareSnapshot(
            DateTimeOffset.UtcNow,
            [
                new SensorReading("cpu-temp", "CPU", SensorKind.CpuTemperature, cpu, "°C"),
                new SensorReading("gpu-temp", "GPU", SensorKind.GpuTemperature, gpu, "°C"),
            ],
            Array.Empty<FanGroup>(),
            IsDemoHardware: true);
    }
}
