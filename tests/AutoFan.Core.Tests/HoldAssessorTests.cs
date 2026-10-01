using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class HoldAssessorTests
{
    [Fact]
    public void Five_flat_seconds_are_not_a_measured_hold()
    {
        HoldAssessment assessment = HoldAssessor.Evaluate(Window(5, cpu: static index => 50), timedOut: false);

        Assert.Equal(HoldAssessment.TransientModeled, assessment);
    }

    [Fact]
    public void Slow_creep_that_looks_flat_for_five_seconds_is_not_measured()
    {
        HoldAssessment assessment = HoldAssessor.Evaluate(
            Window(HoldAssessor.MinimumWindowSamples, cpu: static index => 50 + (index * 0.15)),
            timedOut: false);

        Assert.NotEqual(HoldAssessment.SettledMeasured, assessment);
    }

    [Fact]
    public void Flat_temperatures_and_stable_power_are_measured()
    {
        HoldAssessment assessment = HoldAssessor.Evaluate(
            Window(HoldAssessor.MinimumWindowSamples, cpu: static _ => 50),
            timedOut: false,
            fanGroupId: "front",
            dutyCommanded: true);

        Assert.Equal(HoldAssessment.SettledMeasured, assessment);
    }

    [Fact]
    public void A_power_jump_is_not_stable_even_when_temperature_is_flat()
    {
        HoldAssessment assessment = HoldAssessor.Evaluate(
            Window(HoldAssessor.MinimumWindowSamples, cpu: static _ => 50, cpuPower: static index => index == 0 ? 40 : 80),
            timedOut: false);

        Assert.Equal(HoldAssessment.PowerUnstable, assessment);
    }

    [Fact]
    public void Missing_power_is_not_treated_as_stable_power()
    {
        HoldAssessment assessment = HoldAssessor.Evaluate(
            Window(HoldAssessor.MinimumWindowSamples, cpu: static _ => 50, includePower: false),
            timedOut: false);

        Assert.NotEqual(HoldAssessment.SettledMeasured, assessment);
    }

    [Fact]
    public void A_clock_drop_is_throttling()
    {
        HoldAssessment assessment = HoldAssessor.Evaluate(
            Window(
                HoldAssessor.MinimumWindowSamples,
                cpu: static _ => 50,
                cpuClock: static index => index < 2 ? 4000 : 3000),
            timedOut: false);

        Assert.Equal(HoldAssessment.Throttled, assessment);
    }

    [Fact]
    public void Timeout_before_the_window_is_not_measured()
    {
        HoldAssessment assessment = HoldAssessor.Evaluate(Window(5, cpu: static _ => 50), timedOut: true);

        Assert.Equal(HoldAssessment.TimedOut, assessment);
    }

    [Fact]
    public void A_commanded_fan_with_no_rpm_is_stalled()
    {
        HoldAssessment assessment = HoldAssessor.Evaluate(
            Window(HoldAssessor.MinimumWindowSamples, cpu: static _ => 50, rpm: 0),
            timedOut: false,
            fanGroupId: "front",
            dutyCommanded: true);

        Assert.Equal(HoldAssessment.FanStalled, assessment);
    }

    private static IReadOnlyList<HardwareSnapshot> Window(
        int count,
        Func<int, double> cpu,
        Func<int, double>? cpuPower = null,
        Func<int, double>? cpuClock = null,
        bool includePower = true,
        double rpm = 900)
    {
        var snapshots = new List<HardwareSnapshot>(count);
        DateTimeOffset start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        for (int index = 0; index < count; index++)
        {
            var sensors = new List<SensorReading>
            {
                new("cpu", "CPU Package", SensorKind.CpuTemperature, cpu(index), "°C"),
                new("gpu", "GPU", SensorKind.GpuTemperature, 45, "°C"),
                new("cpu-load", "CPU Load", SensorKind.CpuLoad, 25, "%"),
                new("gpu-load", "GPU Load", SensorKind.GpuLoad, 40, "%"),
            };
            if (includePower)
            {
                sensors.Add(new SensorReading("cpu-power", "CPU Package", SensorKind.CpuPower, cpuPower?.Invoke(index) ?? 80, "W"));
                sensors.Add(new SensorReading("gpu-power", "GPU Package", SensorKind.GpuPower, 120, "W"));
            }

            sensors.Add(new SensorReading(
                "cpu-clock",
                "CPU Clock",
                SensorKind.CpuClock,
                cpuClock?.Invoke(index) ?? 4000,
                "MHz"));
            sensors.Add(new SensorReading("gpu-clock", "GPU Clock", SensorKind.GpuClock, 1800, "MHz"));
            snapshots.Add(new HardwareSnapshot(
                start.AddSeconds(index),
                sensors,
                [new FanGroup("front", "Front", 40, rpm, "Demo", IsControllable: true)],
                IsDemoHardware: true));
        }

        return snapshots;
    }
}
