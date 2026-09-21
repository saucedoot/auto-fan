using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class ThermalTrendTests
{
    [Fact]
    public void Sudden_climb_near_the_ceiling_is_a_rate_of_rise_abort()
    {
        var trend = new ThermalTrend();
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        trend.Add(start, Snapshot(82, 50));
        trend.Add(start.AddSeconds(1), Snapshot(86, 50));
        trend.Add(start.AddSeconds(2), Snapshot(90, 50));
        trend.Add(start.AddSeconds(3), Snapshot(94, 50));

        Assert.Equal(ThermalAbortReason.RateOfRise, trend.Evaluate());
        Assert.Contains("too quickly", SafetyLimits.Describe(ThermalAbortReason.RateOfRise), StringComparison.Ordinal);
    }

    [Fact]
    public void Sudden_climb_from_a_cool_start_is_not_an_abort()
    {
        var trend = new ThermalTrend();
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-20T06:00:00Z");
        trend.Add(start, Snapshot(50, 50));
        trend.Add(start.AddSeconds(1), Snapshot(54, 50));
        trend.Add(start.AddSeconds(2), Snapshot(58, 50));
        trend.Add(start.AddSeconds(3), Snapshot(62, 50));

        Assert.Null(trend.Evaluate());
    }

    [Fact]
    public void Mid_range_cpu_spike_is_not_an_abort()
    {
        var trend = new ThermalTrend();
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-20T06:00:00Z");
        trend.Add(start, Snapshot(64, 35));
        trend.Add(start.AddSeconds(1), Snapshot(69, 35));
        trend.Add(start.AddSeconds(2), Snapshot(74, 35));
        trend.Add(start.AddSeconds(3), Snapshot(78, 35));

        Assert.Null(trend.Evaluate());
    }

    [Fact]
    public void Fast_climb_within_five_of_the_ceiling_is_still_an_abort()
    {
        var trend = new ThermalTrend();
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-20T06:00:00Z");
        trend.Add(start, Snapshot(86, 50));
        trend.Add(start.AddSeconds(1), Snapshot(90, 50));
        trend.Add(start.AddSeconds(2), Snapshot(94, 50));
        trend.Add(start.AddSeconds(3), Snapshot(98, 50));

        Assert.Equal(ThermalAbortReason.RateOfRise, trend.Evaluate());
    }

    [Fact]
    public void Slow_rise_near_the_ceiling_is_not_an_abort()
    {
        var trend = new ThermalTrend();
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-19T12:00:00Z");
        trend.Add(start, Snapshot(86, 50));
        trend.Add(start.AddSeconds(1), Snapshot(86.5, 50.2));
        trend.Add(start.AddSeconds(2), Snapshot(87, 50.4));
        trend.Add(start.AddSeconds(3), Snapshot(87.5, 50.6));

        Assert.Null(trend.Evaluate());
    }

    [Fact]
    public void User_tighter_gpu_abort_arms_rate_of_rise_sooner()
    {
        var trend = new ThermalTrend();
        DateTimeOffset start = DateTimeOffset.Parse("2026-09-20T06:00:00Z");
        ThermalAbortLimits limits = ThermalAbortLimits.FromUser(cpuCelsius: 90, gpuCelsius: 80);
        trend.Add(start, Snapshot(50, 64));
        trend.Add(start.AddSeconds(1), Snapshot(50, 68));
        trend.Add(start.AddSeconds(2), Snapshot(50, 72));
        trend.Add(start.AddSeconds(3), Snapshot(50, 76));

        Assert.Equal(ThermalAbortReason.RateOfRise, trend.Evaluate(limits));
        Assert.Null(trend.Evaluate(ThermalAbortLimits.Floor));
    }

    private static HardwareSnapshot Snapshot(double cpu, double gpu) =>
        new(
            DateTimeOffset.UtcNow,
            [
                new SensorReading(FakeHardwareBackend.CpuSensorId, "CPU", SensorKind.CpuTemperature, cpu, "°C"),
                new SensorReading(FakeHardwareBackend.GpuSensorId, "GPU", SensorKind.GpuTemperature, gpu, "°C"),
            ],
            [],
            IsDemoHardware: true);
}
