using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class ReferenceStabilityTests
{
    [Fact]
    public void Assess_small_scatter_is_stable_with_product_floor_mde()
    {
        SensorStabilityResult result = ReferenceStability.Assess(
            SensorKind.GpuTemperature,
            [61.1, 61.4, 61.2]);

        Assert.Equal(SensorStability.Stable, result.State);
        Assert.Equal(0.3, result.RangeCelsius ?? 0, 1);
        Assert.Equal(0.6, result.MinimumDetectableCelsius, 1);
    }

    [Fact]
    public void Assess_monotonic_climb_is_drifting()
    {
        SensorStabilityResult result = ReferenceStability.Assess(
            SensorKind.CpuTemperature,
            [60.8, 61.6, 62.4]);

        Assert.Equal(SensorStability.Drifting, result.State);
        Assert.Equal(1.6, result.RangeCelsius ?? 0, 3);
        Assert.Equal(3.2, result.MinimumDetectableCelsius, 3);
    }

    [Fact]
    public void Assess_wide_non_monotonic_scatter_is_noisy()
    {
        SensorStabilityResult result = ReferenceStability.Assess(
            SensorKind.GpuTemperature,
            [60.8, 62.5, 61.0]);

        Assert.Equal(SensorStability.Noisy, result.State);
        Assert.True(result.RangeCelsius >= 1.5);
    }

    [Fact]
    public void Assess_missing_holds_are_unavailable()
    {
        SensorStabilityResult result = ReferenceStability.Assess(
            SensorKind.GpuTemperature,
            [61.1, null, 61.2]);

        Assert.Equal(SensorStability.Unavailable, result.State);
        Assert.Null(result.RangeCelsius);
        Assert.Equal(0.5, result.MinimumDetectableCelsius);
    }

    [Fact]
    public void FromBaseline_round_trips_metrics()
    {
        ReferenceAssessment original = ReferenceStability.FromHolds(
        [
            Snapshot(61.1, 54.0),
            Snapshot(61.4, 54.1),
            Snapshot(61.2, 54.0),
        ]);
        var run = new BaselineRun(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: null,
            GpuLoadAvailable: true,
            [],
            ReferenceStability.ToMetrics(original));

        ReferenceAssessment? loaded = ReferenceStability.FromBaseline(run);

        Assert.NotNull(loaded);
        Assert.Equal(SensorStability.Stable, loaded.Cpu.State);
        Assert.Equal(SensorStability.Stable, loaded.Gpu.State);
        Assert.Equal(original.Gpu.MinimumDetectableCelsius, loaded.Gpu.MinimumDetectableCelsius);
    }

    [Fact]
    public void FromBaseline_old_run_without_state_metrics_is_null()
    {
        var run = new BaselineRun(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: null,
            GpuLoadAvailable: true,
            [],
            [new BaselineMetric(BaselineMetricNames.GpuRiseCelsius, 15, "°C", MetricEvidence.Measured)]);

        Assert.Null(ReferenceStability.FromBaseline(run));
        Assert.Null(ReferenceStability.FromBaseline(null));
    }

    [Fact]
    public void WithoutUnusableTargets_marks_drifting_cpu_unknown()
    {
        DateTimeOffset start = new(2026, 9, 20, 14, 0, 0, TimeSpan.Zero);
        IReadOnlyList<InfluenceEntry> map = InfluenceMapBuilder.Build(
        [
            Sample(start, 70, 72),
            .. Speed(start.AddSeconds(10), 66, 71.6),
        ]);
        ReferenceAssessment assessment = new(
            new SensorStabilityResult(
                SensorKind.CpuTemperature,
                SensorStability.Drifting,
                1.6,
                3.2,
                [60.8, 61.6, 62.4]),
            new SensorStabilityResult(
                SensorKind.GpuTemperature,
                SensorStability.Stable,
                0.3,
                0.5,
                [61.1, 61.4, 61.2]));

        IReadOnlyList<InfluenceEntry> gated = ReferenceStability.WithoutUnusableTargets(map, assessment);

        InfluenceEntry cpu = gated.Single(
            entry => entry.Target == InfluenceTarget.Cpu && entry.FanGroupId == FakeHardwareBackend.FrontFanId);
        InfluenceEntry gpu = gated.Single(
            entry => entry.Target == InfluenceTarget.Gpu && entry.FanGroupId == FakeHardwareBackend.FrontFanId);
        Assert.Equal(MetricEvidence.Unknown, cpu.Evidence);
        Assert.Equal(FanTestReasons.CpuNotStable, cpu.SkipReason);
        Assert.Equal(MetricEvidence.Measured, gpu.Evidence);
    }

    [Fact]
    public void Build_treats_delta_below_mde_as_none()
    {
        DateTimeOffset start = new(2026, 9, 20, 14, 0, 0, TimeSpan.Zero);
        ReferenceAssessment assessment = new(
            new SensorStabilityResult(
                SensorKind.CpuTemperature,
                SensorStability.Stable,
                0.2,
                0.5,
                [70, 70.1, 70]),
            new SensorStabilityResult(
                SensorKind.GpuTemperature,
                SensorStability.Stable,
                0.35,
                0.7,
                [72, 72.2, 71.9]));

        IReadOnlyList<InfluenceEntry> map = InfluenceMapBuilder.Build(
            [
                Sample(start, 70, 72),
                .. Speed(start.AddSeconds(10), 69.8, 71.6),
            ],
            assessment);

        InfluenceEntry gpu = map.Single(
            entry => entry.Target == InfluenceTarget.Gpu && entry.FanGroupId == FakeHardwareBackend.FrontFanId);
        Assert.Equal(MetricEvidence.Measured, gpu.Evidence);
        Assert.Equal(InfluenceEffect.None, gpu.Effect);
        Assert.False(InfluenceMapBuilder.MovedATemperature(map, FakeHardwareBackend.FrontFanId));
    }

    private static HardwareSnapshot Snapshot(double cpu, double gpu) =>
        new(
            DateTimeOffset.UnixEpoch,
            [
                new SensorReading("cpu", "CPU", SensorKind.CpuTemperature, cpu, "°C"),
                new SensorReading("gpu", "GPU", SensorKind.GpuTemperature, gpu, "°C"),
            ],
            [],
            IsDemoHardware: true);

    private static FanTestSample Sample(DateTimeOffset at, double cpu, double gpu) =>
        new(
            at,
            FakeHardwareBackend.FrontFanId,
            "Front intake",
            FanTestStage.Reference,
            SnapshotAt(at, cpu, gpu, duty: 40, rpm: 800));

    private static IReadOnlyList<FanTestSample> Speed(DateTimeOffset start, double cpu, double gpu)
    {
        var samples = new List<FanTestSample>(ThermalDynamics.SettleWindowSamples);
        for (int index = 0; index < ThermalDynamics.SettleWindowSamples; index++)
        {
            samples.Add(new FanTestSample(
                start.AddSeconds(index),
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                FanTestStage.Perturb,
                SnapshotAt(start.AddSeconds(index), cpu, gpu, duty: 70, rpm: 1400)));
        }

        return samples;
    }

    private static HardwareSnapshot SnapshotAt(DateTimeOffset at, double cpu, double gpu, int duty, double rpm) =>
        new(
            at,
            [
                new SensorReading("cpu", "CPU", SensorKind.CpuTemperature, cpu, "°C"),
                new SensorReading("gpu", "GPU", SensorKind.GpuTemperature, gpu, "°C"),
                new SensorReading("vrm", "VRM", SensorKind.VrmTemperature, 60, "°C"),
            ],
            [new FanGroup(FakeHardwareBackend.FrontFanId, "Front intake", duty, rpm, "Demo", IsControllable: true)],
            IsDemoHardware: true);
}
