using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class DiminishingReturnsAnalyzerTests
{
    [Fact]
    public void App_md_curve_marks_plateau_and_never_recommends_1200()
    {
        DiminishingReturnsReport report = DiminishingReturnsAnalyzer.Analyze(
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
        ]);

        DiminishingReturnsBand band = Assert.Single(report.Bands);
        Assert.True(report.HasRecommendation);
        Assert.Equal(900, band.RecommendedRpm);
        Assert.NotEqual(1200, band.RecommendedRpm);
        Assert.Equal(900, band.UsefulRpmMin);
        Assert.Equal(1000, band.UsefulRpmMax);
        Assert.Equal(1200, band.WastedRpmMin);
        Assert.Equal(1200, band.WastedRpmMax);
        Assert.Equal(MetricEvidence.Measured, band.Evidence);
        Assert.Contains("900 RPM", band.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_points_like_a_fan_test_stay_unknown()
    {
        DiminishingReturnsReport report = DiminishingReturnsAnalyzer.Analyze(
        [
            new FanSpeedCurve(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Cpu,
                [
                    new FanSpeedPoint(490, 74.2),
                    new FanSpeedPoint(840, 71.8),
                ],
                MetricEvidence.Measured),
        ]);

        DiminishingReturnsBand band = Assert.Single(report.Bands);
        Assert.False(report.HasRecommendation);
        Assert.Null(band.RecommendedRpm);
        Assert.Null(band.UsefulRpmMin);
        Assert.Null(band.WastedRpmMin);
        Assert.Equal(MetricEvidence.Unknown, band.Evidence);
        Assert.Contains("three measured fan speeds", band.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Still_improving_curve_recommends_the_last_point()
    {
        DiminishingReturnsReport report = DiminishingReturnsAnalyzer.Analyze(
        [
            new FanSpeedCurve(
                FakeHardwareBackend.RearFanId,
                "Rear exhaust",
                InfluenceTarget.Cpu,
                [
                    new FanSpeedPoint(600, 80.0),
                    new FanSpeedPoint(800, 76.0),
                    new FanSpeedPoint(1000, 72.0),
                    new FanSpeedPoint(1200, 68.0),
                ],
                MetricEvidence.Measured),
        ]);

        DiminishingReturnsBand band = Assert.Single(report.Bands);
        Assert.Equal(1200, band.RecommendedRpm);
        Assert.Equal(1200, band.UsefulRpmMin);
        Assert.Equal(1200, band.UsefulRpmMax);
        Assert.Null(band.WastedRpmMin);
        Assert.Null(band.WastedRpmMax);
        Assert.Equal(MetricEvidence.Measured, band.Evidence);
    }

    [Fact]
    public void Louder_hotter_tail_is_wasted_not_recommended()
    {
        DiminishingReturnsReport report = DiminishingReturnsAnalyzer.Analyze(
        [
            new FanSpeedCurve(
                FakeHardwareBackend.TopFanId,
                "Top exhaust",
                InfluenceTarget.Gpu,
                [
                    new FanSpeedPoint(600, 74.0),
                    new FanSpeedPoint(800, 70.0),
                    new FanSpeedPoint(1000, 69.0),
                    new FanSpeedPoint(1200, 72.0),
                ],
                MetricEvidence.Measured),
        ]);

        DiminishingReturnsBand band = Assert.Single(report.Bands);
        Assert.Equal(800, band.RecommendedRpm);
        Assert.NotEqual(1200, band.RecommendedRpm);
        Assert.Equal(800, band.UsefulRpmMin);
        Assert.Equal(1000, band.UsefulRpmMax);
        Assert.Equal(1200, band.WastedRpmMin);
        Assert.Equal(1200, band.WastedRpmMax);
        Assert.Equal(MetricEvidence.Measured, band.Evidence);
    }

    [Fact]
    public void Empty_input_is_an_empty_report()
    {
        DiminishingReturnsReport report = DiminishingReturnsAnalyzer.Analyze([]);

        Assert.Empty(report.Bands);
        Assert.False(report.HasRecommendation);
    }
}
