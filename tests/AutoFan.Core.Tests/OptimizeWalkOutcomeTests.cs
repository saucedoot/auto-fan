using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class OptimizeWalkOutcomeTests
{
    [Fact]
    public void Watch_continue_only_after_a_completed_run()
    {
        Assert.True(OptimizeWalkOutcome.ContinueAfterWatch(BaselineRunStatus.Completed));
        Assert.False(OptimizeWalkOutcome.ContinueAfterWatch(BaselineRunStatus.Aborted));
        Assert.False(OptimizeWalkOutcome.ContinueAfterWatch(BaselineRunStatus.Cancelled));
    }

    [Fact]
    public void Fan_tests_do_not_continue_when_aborted_with_no_influence()
    {
        Assert.False(OptimizeWalkOutcome.ContinueAfterFans(FanTestRunStatus.Aborted, []));
        Assert.False(OptimizeWalkOutcome.ContinueAfterFans(FanTestRunStatus.Completed, []));
        Assert.False(OptimizeWalkOutcome.ContinueAfterFans(FanTestRunStatus.Aborted, null));
    }

    [Fact]
    public void Fan_tests_continue_after_abort_when_a_group_was_measured()
    {
        InfluenceEntry[] influence =
        [
            new(
                "cpu-fan",
                "CPU Fan",
                InfluenceTarget.Cpu,
                2.4,
                InfluenceEffect.Medium,
                MetricEvidence.Measured,
                40,
                70,
                800,
                1200),
        ];

        Assert.True(OptimizeWalkOutcome.ContinueAfterFans(FanTestRunStatus.Aborted, influence));
        Assert.True(OptimizeWalkOutcome.ContinueAfterFans(FanTestRunStatus.Cancelled, influence));
        Assert.True(OptimizeWalkOutcome.ContinueAfterFans(FanTestRunStatus.Completed, influence));
        Assert.Equal(WalkFansNext.SkipPairs, OptimizeWalkOutcome.AfterFans(Run(FanTestRunStatus.Aborted, influence)));
        Assert.Equal(WalkFansNext.SkipPairs, OptimizeWalkOutcome.AfterFans(Run(FanTestRunStatus.Cancelled, influence)));
        Assert.Equal(WalkFansNext.RunPairs, OptimizeWalkOutcome.AfterFans(Run(FanTestRunStatus.Completed, influence)));
        Assert.False(OptimizeWalkOutcome.ShouldRunPairs(Run(FanTestRunStatus.Aborted, influence)));
    }

    [Fact]
    public void Thermal_abort_with_a_map_skips_pairs_and_duration_cap_does_too()
    {
        InfluenceEntry[] influence = [MeasuredCpu()];
        FanTestRun rise = Run(
            FanTestRunStatus.Aborted,
            influence,
            SafetyLimits.Describe(ThermalAbortReason.RateOfRise));
        FanTestRun duration = Run(
            FanTestRunStatus.Aborted,
            influence,
            $"Fan tests reached the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.");

        Assert.Equal(WalkFansNext.SkipPairs, OptimizeWalkOutcome.AfterFans(rise));
        Assert.Equal(WalkFansNext.SkipPairs, OptimizeWalkOutcome.AfterFans(duration));
        Assert.Equal(OptimizeWalkOutcome.SkipPairsDetail, OptimizeWalkOutcome.FansCompleteDetail(rise));
    }

    [Fact]
    public void Lost_temps_gpu_reset_or_competing_software_retry_even_with_a_map()
    {
        InfluenceEntry[] influence = [MeasuredCpu()];
        Assert.Equal(
            WalkFansNext.Retry,
            OptimizeWalkOutcome.AfterFans(
                Run(FanTestRunStatus.Aborted, influence, SafetyLimits.Describe(ThermalAbortReason.TelemetryLost))));
        Assert.Equal(
            WalkFansNext.Retry,
            OptimizeWalkOutcome.AfterFans(
                Run(FanTestRunStatus.Aborted, influence, SafetyLimits.Describe(ThermalAbortReason.GpuDeviceLost))));
        Assert.Equal(
            WalkFansNext.Retry,
            OptimizeWalkOutcome.AfterFans(
                Run(
                    FanTestRunStatus.Aborted,
                    influence,
                    "Competing fan software is running: FanControl. Close it, then try again.")));
    }

    [Fact]
    public void Watch_complete_notes_when_gpu_core_did_not_rise_enough()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-09-20T06:00:00Z");
        var cool = new BaselineRun(
            Guid.NewGuid(),
            at,
            at,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: null,
            GpuLoadAvailable: true,
            [],
            [new BaselineMetric(BaselineMetricNames.GpuRiseCelsius, 2.6, "°C", MetricEvidence.Measured)]);
        var hot = cool with
        {
            Metrics =
            [
                new BaselineMetric(
                    BaselineMetricNames.GpuRiseCelsius,
                    HeatCalibrator.TargetRiseCelsius,
                    "°C",
                    MetricEvidence.Measured),
            ],
        };

        Assert.Contains("Unknown", OptimizeWalkOutcome.WatchCompleteDetail(cool), StringComparison.Ordinal);
        Assert.Equal("Watching finished. Continue when you are ready.", OptimizeWalkOutcome.WatchCompleteDetail(hot));
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
        BaselineRun noted = hot with
        {
            Metrics = [.. hot.Metrics, .. ReferenceStability.ToMetrics(assessment)],
        };
        string detail = OptimizeWalkOutcome.WatchCompleteDetail(noted);
        Assert.Contains("GPU temperatures were steady", detail, StringComparison.Ordinal);
        Assert.Contains("CPU wandered", detail, StringComparison.Ordinal);
        Assert.False(GpuHeat.IsUseful(cool));
        Assert.True(GpuHeat.IsUseful(hot));
        Assert.False(GpuHeat.IsUseful(null));
    }

    [Fact]
    public void Unknown_influence_is_not_useful()
    {
        InfluenceEntry[] unknown =
        [
            new(
                "cpu-fan",
                "CPU Fan",
                InfluenceTarget.Cpu,
                DeltaCelsius: null,
                Effect: null,
                MetricEvidence.Unknown,
                40,
                DutyAfter: null,
                0,
                RpmAfter: null,
                SkipReason: FanTestReasons.NoRpm),
        ];

        Assert.False(OptimizeWalkOutcome.HasUsefulInfluence(unknown));
        Assert.False(OptimizeWalkOutcome.ContinueAfterFans(FanTestRunStatus.Aborted, unknown));
    }

    [Fact]
    public void Fail_details_prefer_the_abort_sentence()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-09-20T06:00:00Z");
        var baseline = new BaselineRun(
            Guid.NewGuid(),
            at,
            at,
            BaselineRunStatus.Aborted,
            "CPU temperature reached the 90 °C abort limit.",
            AmbientCelsius: null,
            GpuLoadAvailable: true,
            [],
            []);
        var emptyFans = new FanTestRun(
            Guid.NewGuid(),
            at,
            at,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [],
            []);

        Assert.Contains("90", OptimizeWalkOutcome.WatchFailDetail(baseline), StringComparison.Ordinal);
        Assert.Equal(OptimizeWalkOutcome.EmptyFanTestsDetail, OptimizeWalkOutcome.FansFailDetail(emptyFans));
    }

    private static InfluenceEntry MeasuredCpu() =>
        new(
            "cpu-fan",
            "CPU Fan",
            InfluenceTarget.Cpu,
            2.4,
            InfluenceEffect.Medium,
            MetricEvidence.Measured,
            40,
            70,
            800,
            1200);

    private static FanTestRun Run(
        FanTestRunStatus status,
        IReadOnlyList<InfluenceEntry> influence,
        string? abortDetail = null)
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-09-20T06:00:00Z");
        return new FanTestRun(
            Guid.NewGuid(),
            at,
            at,
            status,
            abortDetail,
            GpuLoadAvailable: true,
            [],
            influence,
            []);
    }
}
