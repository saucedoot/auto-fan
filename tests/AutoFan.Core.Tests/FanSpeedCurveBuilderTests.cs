using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class FanSpeedCurveBuilderTests
{
    [Fact]
    public void Build_plots_settled_speed_holds_not_bios_or_flicker()
    {
        DateTimeOffset start = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);
        FanTestSample[] samples =
        [
            .. Hold(start, 60, 1265, 59.2, FanTestStage.Reference),
            .. Hold(start.AddMinutes(1), 70, 1466, 60.3, FanTestStage.Perturb),
            Sample(start.AddMinutes(2), 73, 1500, 72.0, FanTestStage.Screen, settled: false),
            Sample(start.AddMinutes(2).AddSeconds(1), 75, 1510, 73.0, FanTestStage.Screen, settled: false),
            Sample(start.AddMinutes(2).AddSeconds(2), 78, 1520, 74.0, FanTestStage.Screen, settled: false),
            .. Hold(start.AddMinutes(3), 100, 1785, 63.7, FanTestStage.Screen),
            .. Hold(start.AddMinutes(4), 85, 1700, 63.0, FanTestStage.Refine),
        ];

        IReadOnlyList<FanSpeedCurve> curves = FanSpeedCurveBuilder.Build(samples);

        FanSpeedCurve cpu = Assert.Single(curves, curve => curve.Target == InfluenceTarget.Cpu);
        Assert.Equal(
            [1466, 1700, 1785],
            cpu.Points.Select(static point => point.Rpm).ToArray());
        Assert.DoesNotContain(cpu.Points, static point => point.Rpm is 1265 or 1500 or 1510 or 1520);
        Assert.Equal(60.3, cpu.Points[0].TempCelsius, 1);
        Assert.Equal(63.7, cpu.Points[^1].TempCelsius, 1);
    }

    [Fact]
    public void Build_skips_stalled_zero_rpm_holds()
    {
        DateTimeOffset start = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);
        FanTestSample[] samples =
        [
            .. Hold(start, 40, 560, 72.0, FanTestStage.Perturb),
            .. Hold(start.AddMinutes(1), 70, 0, 71.0, FanTestStage.Screen),
            .. Hold(start.AddMinutes(2), 100, 1400, 69.5, FanTestStage.Screen),
        ];

        IReadOnlyList<FanSpeedCurve> curves = FanSpeedCurveBuilder.Build(samples);

        FanSpeedCurve cpu = Assert.Single(curves, curve => curve.Target == InfluenceTarget.Cpu);
        Assert.Equal(2, cpu.Points.Count);
        Assert.DoesNotContain(cpu.Points, static point => point.Rpm == 0);
        Assert.Equal(560, cpu.Points[0].Rpm);
        Assert.Equal(1400, cpu.Points[^1].Rpm);
    }

    [Fact]
    public void Empty_samples_yield_no_curves()
    {
        Assert.Empty(FanSpeedCurveBuilder.Build([]));
        Assert.Empty(FanSpeedCurveBuilder.Build(null));
    }

    [Fact]
    public void Build_skips_timeout_holds_even_when_temps_look_flat()
    {
        DateTimeOffset start = new(2026, 9, 21, 1, 0, 0, TimeSpan.Zero);
        FanTestSample[] samples =
        [
            .. Hold(start, 70, 1466, 60.3, FanTestStage.Perturb, settled: false),
            .. Hold(start.AddMinutes(1), 100, 1785, 63.7, FanTestStage.Screen),
        ];

        IReadOnlyList<FanSpeedCurve> curves = FanSpeedCurveBuilder.Build(samples);

        FanSpeedCurve cpu = Assert.Single(curves, curve => curve.Target == InfluenceTarget.Cpu);
        Assert.Equal([1785], cpu.Points.Select(static point => point.Rpm).ToArray());
        Assert.DoesNotContain(cpu.Points, static point => point.Rpm == 1466);
    }

    private static FanTestSample[] Hold(
        DateTimeOffset start,
        int duty,
        double rpm,
        double cpu,
        FanTestStage stage,
        bool settled = true)
    {
        var samples = new FanTestSample[ThermalDynamics.SettleWindowSamples];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = Sample(start.AddSeconds(index), duty, rpm, cpu, stage, settled);
        }

        return samples;
    }

    private static FanTestSample Sample(
        DateTimeOffset at,
        int duty,
        double rpm,
        double cpu,
        FanTestStage stage,
        bool settled = true) =>
        new(
            at,
            FakeHardwareBackend.FrontFanId,
            "Front intake",
            stage,
            new HardwareSnapshot(
                at,
                [
                    new SensorReading("cpu-temp", "CPU", SensorKind.CpuTemperature, cpu, "°C"),
                    new SensorReading("gpu-temp", "GPU", SensorKind.GpuTemperature, 60, "°C"),
                ],
                [
                    new FanGroup(
                        FakeHardwareBackend.FrontFanId,
                        "Front intake",
                        duty,
                        rpm,
                        "Demo controller",
                        IsControllable: true),
                ],
                IsDemoHardware: true),
            settled);
}
