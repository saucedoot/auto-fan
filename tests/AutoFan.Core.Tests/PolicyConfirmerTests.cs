using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class PolicyConfirmerTests
{
    [Fact]
    public void Miss_raises_when_live_is_clearly_hotter_than_predicted()
    {
        Assert.True(PolicyConfirmer.IsMiss(80, 76));
        Assert.False(PolicyConfirmer.IsMiss(76.5, 76));
        Assert.True(PolicyConfirmer.Missed(80, 76, 60, 60));
    }

    [Fact]
    public void Expected_settled_subtracts_modeled_cooling()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            new HardwareIdentity("mb", "cpu", "gpu", "mb"),
            baseline: null,
            new FanTestRun(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                FanTestRunStatus.Completed,
                AbortDetail: null,
                GpuLoadAvailable: true,
                [],
                [
                    new InfluenceEntry(
                        FakeHardwareBackend.FrontFanId,
                        "Front intake",
                        InfluenceTarget.Cpu,
                        2.0,
                        InfluenceEffect.Medium,
                        MetricEvidence.Measured,
                        35,
                        60,
                        490,
                        840),
                ],
                []),
            interaction: null);
        CoolingPolicy? policy = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            CoolingPreferences.Default);

        Assert.NotNull(policy);
        (double? cpu, double? gpu) = PolicyConfirmer.ExpectedSettled(70, 65, model, policy);

        Assert.Equal(68, cpu);
        Assert.Null(gpu);
    }

    [Fact]
    public void Gpu_expected_does_not_plunge_below_start_when_already_cooler_than_idle()
    {
        ThermalModel model = GpuPolicyModel(idleGpu: 46, cooling: 22);
        CoolingPolicy? policy = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            CoolingPreferences.Default);

        Assert.NotNull(policy);
        (double? cpu, double? gpu) = PolicyConfirmer.ExpectedSettled(60, 42, model, policy);

        Assert.Equal(42, gpu);
        Assert.Null(cpu);
    }

    [Fact]
    public void Gpu_expected_subtracts_normally_when_start_is_above_idle()
    {
        ThermalModel model = GpuPolicyModel(idleGpu: 46, cooling: 5);
        CoolingPolicy? policy = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            CoolingPreferences.Default);

        Assert.NotNull(policy);
        (_, double? gpu) = PolicyConfirmer.ExpectedSettled(60, 70, model, policy);

        Assert.Equal(65, gpu);
    }

    [Fact]
    public void Gpu_expected_does_not_go_below_idle_when_start_is_hotter()
    {
        ThermalModel model = GpuPolicyModel(idleGpu: 46, cooling: 40);
        CoolingPolicy? policy = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            CoolingPreferences.Default);

        Assert.NotNull(policy);
        (_, double? gpu) = PolicyConfirmer.ExpectedSettled(60, 70, model, policy);

        Assert.Equal(46, gpu);
    }

    private static ThermalModel GpuPolicyModel(double idleGpu, double cooling)
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var baseline = new BaselineRun(
            Guid.NewGuid(),
            at,
            at,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: null,
            GpuLoadAvailable: true,
            [
                new BaselineSample(
                    at,
                    BaselinePhase.Idle,
                    new HardwareSnapshot(
                        at,
                        [
                            new SensorReading(
                                "gpu-core",
                                "GPU Core",
                                SensorKind.GpuTemperature,
                                idleGpu,
                                "°C"),
                        ],
                        [],
                        IsDemoHardware: true)),
            ],
            []);
        return ThermalModelFitter.Fit(
            new HardwareIdentity("mb", "cpu", "gpu", "mb"),
            baseline,
            new FanTestRun(
                Guid.NewGuid(),
                at,
                at,
                FanTestRunStatus.Completed,
                AbortDetail: null,
                GpuLoadAvailable: true,
                [],
                [
                    new InfluenceEntry(
                        FakeHardwareBackend.FrontFanId,
                        "Front intake",
                        InfluenceTarget.Gpu,
                        cooling,
                        InfluenceEffect.VeryHigh,
                        MetricEvidence.Measured,
                        30,
                        85,
                        1130,
                        3360),
                ],
                []),
            interaction: null);
    }
}
