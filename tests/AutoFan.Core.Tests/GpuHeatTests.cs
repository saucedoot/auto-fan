using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class GpuHeatTests
{
    [Fact]
    public void Everyday_rise_under_fifteen_is_not_useful()
    {
        var run = new BaselineRun(
            Guid.Empty,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: null,
            GpuLoadAvailable: true,
            [],
            [
                new BaselineMetric(BaselineMetricNames.GpuEverydayRiseCelsius, 2.1, "°C", MetricEvidence.Measured),
                new BaselineMetric(BaselineMetricNames.GpuRiseCelsius, 15.1, "°C", MetricEvidence.Measured),
            ]);

        Assert.True(GpuHeat.IsUseful(run));
        Assert.False(GpuHeat.EverydayIsUseful(run));
    }

    [Fact]
    public void Everyday_rise_of_fifteen_is_useful()
    {
        var run = new BaselineRun(
            Guid.Empty,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: null,
            GpuLoadAvailable: true,
            [],
            [new BaselineMetric(BaselineMetricNames.GpuEverydayRiseCelsius, 15, "°C", MetricEvidence.Measured)]);

        Assert.True(GpuHeat.EverydayIsUseful(run));
    }
}
