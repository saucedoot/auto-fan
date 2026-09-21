using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class MeasuredRpmPlotTests
{
    [Fact]
    public void App_md_curve_keeps_points_and_marks_the_plateau()
    {
        IReadOnlyList<FanSpeedCurve> curves =
        [
            new FanSpeedCurve(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Cpu,
                [
                    new FanSpeedPoint(600, 74.2),
                    new FanSpeedPoint(700, 71.8),
                    new FanSpeedPoint(800, 70.5),
                    new FanSpeedPoint(900, 69.9),
                    new FanSpeedPoint(1000, 69.5),
                    new FanSpeedPoint(1200, 69.2),
                ],
                MetricEvidence.Measured),
        ];
        DiminishingReturnsReport report = DiminishingReturnsAnalyzer.Analyze(curves);

        MeasuredRpmPlot plot = MeasuredRpmPlot.For(
            curves,
            report,
            FakeHardwareBackend.FrontFanId,
            InfluenceTarget.Cpu);

        Assert.True(plot.HasPoints);
        Assert.Equal(6, plot.Points.Count);
        Assert.Equal(900, plot.RecommendedRpm);
        Assert.Equal(900, plot.UsefulRpmMin);
        Assert.Equal(1000, plot.UsefulRpmMax);
        Assert.Equal(1200, plot.WastedRpmMin);
        Assert.True(plot.RpmMin < 600);
        Assert.True(plot.RpmMax > 1200);
        Assert.True(plot.IsOnPlateau(plot.Points[^1]));
        Assert.False(plot.IsOnPlateau(plot.Points[0]));
        Assert.Equal(string.Empty, plot.EmptyReason);
        Assert.Equal("Front intake  ·  CPU", plot.Title);
        Assert.Equal(MeasuredRpmPlot.HowToText, plot.HowTo);
        Assert.Equal("Recommended  900 RPM", plot.RecommendedLabel);
        Assert.Contains("900 RPM", plot.Caption, StringComparison.Ordinal);
        Assert.Contains("1 °C", plot.Caption, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_rpm_points_are_omitted()
    {
        DateTimeOffset start = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);
        IReadOnlyList<FanSpeedCurve> curves = FanSpeedCurveBuilder.Build(
        [
            .. Hold(start, 35, 490, 74.2, FanTestStage.Reference),
            .. Hold(start.AddMinutes(1), 40, 560, 72.0, FanTestStage.Perturb),
            .. Hold(start.AddMinutes(2), 70, 0, 71.0, FanTestStage.Screen),
            .. Hold(start.AddMinutes(3), 100, 1400, 69.5, FanTestStage.Screen),
        ]);

        MeasuredRpmPlot plot = MeasuredRpmPlot.For(
            curves,
            DiminishingReturnsAnalyzer.Analyze(curves),
            FakeHardwareBackend.FrontFanId,
            InfluenceTarget.Cpu);

        Assert.DoesNotContain(plot.Points, static point => point.Rpm == 0);
        Assert.Equal(2, plot.Points.Count);
        Assert.Equal(560, plot.Points[0].Rpm);
        Assert.Equal(1400, plot.Points[^1].Rpm);
    }

    [Fact]
    public void Gpu_sample_points_plot_even_without_a_gpu_influence_map()
    {
        IReadOnlyList<FanSpeedCurve> curves =
        [
            new FanSpeedCurve(
                FakeHardwareBackend.TopFanId,
                "System Fan #1",
                InfluenceTarget.Gpu,
                [
                    new FanSpeedPoint(979, 53.0),
                    new FanSpeedPoint(1063, 51.6),
                    new FanSpeedPoint(1377, 50.2),
                ],
                MetricEvidence.Measured),
        ];

        MeasuredRpmPlot plot = MeasuredRpmPlot.For(
            curves,
            DiminishingReturnsAnalyzer.Analyze(curves),
            FakeHardwareBackend.TopFanId,
            InfluenceTarget.Gpu);

        Assert.True(plot.HasPoints);
        Assert.Equal(3, plot.Points.Count);
        Assert.Equal(FakeHardwareBackend.TopFanId, plot.FanGroupId);
        Assert.Equal(InfluenceTarget.Gpu, plot.Target);
    }

    [Fact]
    public void Empty_curves_ask_for_fan_tests()
    {
        MeasuredRpmPlot plot = MeasuredRpmPlot.For(
            [],
            DiminishingReturnsAnalyzer.Analyze([]),
            groupId: null,
            InfluenceTarget.Cpu);

        Assert.False(plot.HasPoints);
        Assert.Equal(MeasuredRpmPlot.NeedFanTestsReason, plot.EmptyReason);
        Assert.Equal("Measured RPM vs temperature", plot.Title);
        Assert.Equal(MeasuredRpmPlot.NeedFanTestsReason, plot.HowTo);
        Assert.Equal(MeasuredRpmPlot.NeedFanTestsReason, plot.Caption);
        Assert.Empty(plot.RecommendedLabel);
        Assert.Empty(MeasuredRpmPlot.FanChoices([]));
    }

    [Fact]
    public void Fan_choices_skip_vrm_and_case()
    {
        IReadOnlyList<FanSpeedCurve> curves =
        [
            new FanSpeedCurve(
                FakeHardwareBackend.RearFanId,
                "Rear exhaust",
                InfluenceTarget.Vrm,
                [new FanSpeedPoint(800, 60)],
                MetricEvidence.Measured),
            new FanSpeedCurve(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Cpu,
                [new FanSpeedPoint(900, 70)],
                MetricEvidence.Measured),
        ];

        IReadOnlyList<MeasuredRpmFanChoice> fans = MeasuredRpmPlot.FanChoices(curves);

        Assert.Equal(FakeHardwareBackend.FrontFanId, Assert.Single(fans).FanGroupId);
    }

    [Fact]
    public void Missing_gpu_points_for_a_fan_stay_empty()
    {
        IReadOnlyList<FanSpeedCurve> curves =
        [
            new FanSpeedCurve(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Cpu,
                [new FanSpeedPoint(900, 70)],
                MetricEvidence.Measured),
        ];

        MeasuredRpmPlot plot = MeasuredRpmPlot.For(
            curves,
            DiminishingReturnsAnalyzer.Analyze(curves),
            FakeHardwareBackend.FrontFanId,
            InfluenceTarget.Gpu);

        Assert.False(plot.HasPoints);
        Assert.Contains("GPU", plot.EmptyReason, StringComparison.Ordinal);
        Assert.DoesNotContain("20", plot.EmptyReason, StringComparison.Ordinal);
    }

    private static FanTestSample[] Hold(
        DateTimeOffset start,
        int duty,
        double rpm,
        double cpu,
        FanTestStage stage)
    {
        var samples = new FanTestSample[ThermalDynamics.SettleWindowSamples];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = Sample(start.AddSeconds(index), duty, rpm, cpu, stage);
        }

        return samples;
    }

    private static FanTestSample Sample(
        DateTimeOffset at,
        int duty,
        double rpm,
        double cpu,
        FanTestStage stage) =>
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
            Settled: true);
}
