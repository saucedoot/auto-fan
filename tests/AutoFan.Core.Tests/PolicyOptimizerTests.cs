using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class PolicyOptimizerTests
{
    private static readonly HardwareIdentity Identity = new(
        "ASUS ROG STRIX Z790-E",
        "Intel Core i7-13700K",
        "NVIDIA GeForce RTX 4070",
        "ASUS ROG STRIX Z790-E");

    [Fact]
    public void App_md_curve_never_picks_1200_even_on_cool()
    {
        ThermalModel model = FrontModel();
        DiminishingReturnsReport returns = AppMdReport();
        CoolingPolicy? policy = PolicyOptimizer.Recommend(
            model,
            returns,
            new CoolingPreferences(1, 80, 75));

        Assert.NotNull(policy);
        GroupPolicy front = Assert.Single(policy.Groups);
        int wasted = PolicyOptimizer.DutyForRpm(35, 60, 490, 840, 1200);
        int usefulMax = PolicyOptimizer.DutyForRpm(35, 60, 490, 840, 1000);
        int recommended = PolicyOptimizer.DutyForRpm(35, 60, 490, 840, 900);
        Assert.Equal(usefulMax, front.DutyPercent);
        Assert.Equal(usefulMax, front.CoolDutyPercent);
        Assert.Equal(recommended, front.QuietDutyPercent);
        Assert.NotEqual(wasted, front.DutyPercent);
        Assert.NotEqual(wasted, front.CoolDutyPercent);
        Assert.Equal(1000, front.AppliedRpm);
    }

    [Fact]
    public void Quieter_slider_picks_a_lower_duty_than_cooler_when_temps_are_close()
    {
        ThermalModel model = FrontModel();
        DiminishingReturnsReport returns = AppMdReport();
        CoolingPolicy? quiet = PolicyOptimizer.Recommend(
            model,
            returns,
            new CoolingPreferences(0, 80, 75));
        CoolingPolicy? cool = PolicyOptimizer.Recommend(
            model,
            returns,
            new CoolingPreferences(1, 80, 75));

        Assert.NotNull(quiet);
        Assert.NotNull(cool);
        Assert.True(quiet.Groups[0].DutyPercent < cool.Groups[0].DutyPercent);
        Assert.Equal(PolicyOptimizer.DutyForRpm(35, 60, 490, 840, 900), quiet.Groups[0].DutyPercent);
        Assert.Equal(900, quiet.Groups[0].AppliedRpm);
    }

    [Fact]
    public void Aborted_fan_tests_with_a_useful_group_yield_a_quieter_policy()
    {
        var run = FanTest(
            [
                new InfluenceEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Cpu,
                    2.4,
                    InfluenceEffect.Medium,
                    MetricEvidence.Measured,
                    35,
                    60,
                    490,
                    840),
            ],
            FanTestRunStatus.Aborted);
        ThermalModel model = ThermalModelFitter.Fit(Identity, baseline: null, run, interaction: null);

        CoolingPolicy? cool = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            new CoolingPreferences(1, 80, 75));
        CoolingPolicy? finished = PolicyOptimizer.Recommend(
            FrontModel(),
            DiminishingReturnsAnalyzer.Analyze([]),
            new CoolingPreferences(1, 80, 75));

        Assert.NotNull(cool);
        Assert.NotNull(finished);
        Assert.True(cool.QuietCool <= 0.25);
        Assert.True(cool.Groups[0].DutyPercent < finished.Groups[0].DutyPercent);
    }

    [Fact]
    public void Missing_fan_tests_return_no_policy()
    {
        ThermalModel model = ThermalModelFitter.Fit(Identity, baseline: null, fanTest: null, interaction: null);

        CoolingPolicy? policy = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            CoolingPreferences.Default);

        Assert.Null(policy);
        Assert.Equal(PolicyOptimizer.NeedFanTestsReason, PolicyOptimizer.UnavailableReason(model));
    }

    [Fact]
    public void Without_a_speed_curve_quiet_and_cool_use_the_measured_duties()
    {
        ThermalModel model = FrontModel();
        CoolingPolicy? quiet = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            new CoolingPreferences(0, 80, 75));
        CoolingPolicy? cool = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            new CoolingPreferences(1, 80, 75));

        Assert.NotNull(quiet);
        Assert.NotNull(cool);
        Assert.Equal(35, quiet.Groups[0].DutyPercent);
        Assert.Equal(60, cool.Groups[0].DutyPercent);
        Assert.True(quiet.Groups[0].DutyPercent < cool.Groups[0].DutyPercent);
    }

    [Fact]
    public void None_effect_group_is_skipped_without_a_band()
    {
        var run = FanTest(
        [
            new InfluenceEntry(
                FakeHardwareBackend.RearFanId,
                "Rear exhaust",
                InfluenceTarget.Cpu,
                0.2,
                InfluenceEffect.None,
                MetricEvidence.Measured,
                30,
                55,
                360,
                660),
        ]);
        ThermalModel model = ThermalModelFitter.Fit(Identity, baseline: null, run, interaction: null);

        CoolingPolicy? policy = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            CoolingPreferences.Default);

        Assert.Equal(ModelConfidence.Low, model.Confidence);
        Assert.Null(policy);
        Assert.Contains("changed a temperature", PolicyOptimizer.UnavailableReason(model), StringComparison.Ordinal);
    }

    [Fact]
    public void Low_only_model_does_not_raise_the_recommended_point_for_a_hotter_ask()
    {
        var hardware = new FakeHardwareBackend();
        ThermalModel model = WorkloadPolicyFixtures.GpuFrontModel(hardware);
        CoolingPreferences preferences = CoolingPreferences.Default;

        CoolingPolicy? policy = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            preferences);

        Assert.NotNull(policy);
        Assert.NotEqual(ModelConfidence.High, model.Confidence);
        Assert.Equal(
            PolicyOptimizer.BlendDuty(35, 60, preferences.Blend),
            policy.Groups[0].DutyPercent);
    }

    [Fact]
    public void Targets_stay_below_abort_ceilings()
    {
        var prefs = new CoolingPreferences(0.5, 95, 90);

        Assert.Equal(SafetyLimits.CpuAbortCelsius - 1, prefs.CpuTarget);
        Assert.Equal(SafetyLimits.GpuAbortCelsius - 1, prefs.GpuTarget);
        Assert.True(prefs.CpuTarget < SafetyLimits.CpuAbortCelsius);
        Assert.True(prefs.GpuTarget < SafetyLimits.GpuAbortCelsius);
    }

    [Fact]
    public void Aborted_fan_tests_keep_the_quieter_conservative_blend()
    {
        var cool = new CoolingPreferences(1, 80, 75);
        CoolingPolicy? finished = PolicyOptimizer.Recommend(
            FrontModel(),
            DiminishingReturnsAnalyzer.Analyze([]),
            cool);
        CoolingPolicy? aborted = PolicyOptimizer.Recommend(
            FrontModel(FanTestRunStatus.Aborted),
            DiminishingReturnsAnalyzer.Analyze([]),
            cool);

        Assert.NotNull(finished);
        Assert.NotNull(aborted);
        Assert.Equal(0.25, aborted.QuietCool);
        Assert.True(aborted.Groups[0].DutyPercent < finished.Groups[0].DutyPercent);
    }

    private static ThermalModel FrontModel(FanTestRunStatus status = FanTestRunStatus.Completed)
    {
        var run = FanTest(
        [
            new InfluenceEntry(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Cpu,
                2.4,
                InfluenceEffect.Medium,
                MetricEvidence.Measured,
                35,
                60,
                490,
                840),
        ],
            status);
        return ThermalModelFitter.Fit(Identity, baseline: null, run, interaction: null);
    }

    private static DiminishingReturnsReport AppMdReport() =>
        DiminishingReturnsAnalyzer.Analyze(
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

    private static FanTestRun FanTest(
        IReadOnlyList<InfluenceEntry> influence,
        FanTestRunStatus status = FanTestRunStatus.Completed) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            status,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            influence,
            []);
}
