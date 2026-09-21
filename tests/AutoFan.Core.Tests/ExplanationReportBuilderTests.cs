using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class ExplanationReportBuilderTests
{
    private const string BottomFanId = "bottom-intake";

    private static readonly HardwareIdentity Identity = new(
        "ASUS ROG STRIX Z790-E",
        "Intel Core i7-13700K",
        "NVIDIA GeForce RTX 4070",
        "ASUS ROG STRIX Z790-E");

    [Fact]
    public void App_md_fixture_tags_every_heading()
    {
        ThermalModel model = AppMdModel();
        DiminishingReturnsReport returns = AppMdReturns();
        CoolingPolicy? policy = PolicyOptimizer.Recommend(model, returns, CoolingPreferences.Default);

        ExplanationReport report = ExplanationReportBuilder.Build(model, returns, policy);

        Assert.NotNull(policy);
        Assert.Equal(9, report.Sections.Count);
        Assert.Equal(
            [
                ExplanationReportBuilder.WanderHeading,
                ExplanationReportBuilder.PrimaryGpuPathHeading,
                ExplanationReportBuilder.PrimaryCpuPathHeading,
                ExplanationReportBuilder.MostEffectiveFanHeading,
                ExplanationReportBuilder.DiminishingReturnsHeading,
                ExplanationReportBuilder.DetectedInteractionHeading,
                ExplanationReportBuilder.RecommendedPointHeading,
                ExplanationReportBuilder.ExpectedResultHeading,
                ExplanationReportBuilder.ConfirmationHeading,
            ],
            report.Sections.Select(static section => section.Heading));
        Assert.All(report.Sections, static section => Assert.NotEmpty(section.Claims));
        Assert.All(
            report.Sections.SelectMany(static section => section.Claims),
            static claim =>
            {
                Assert.False(string.IsNullOrWhiteSpace(claim.Title));
                Assert.False(string.IsNullOrWhiteSpace(claim.Text));
            });

        Assert.Equal(
            ExplanationReportBuilder.NeedWatchHolds,
            Section(report, ExplanationReportBuilder.WanderHeading).Claims[0].Text);

        ExplanationSection gpuPath = Section(report, ExplanationReportBuilder.PrimaryGpuPathHeading);
        Assert.Contains(gpuPath.Claims, claim =>
            claim.Evidence == MetricEvidence.Inferred
            && claim.Text.Contains("Bottom intake", StringComparison.Ordinal)
            && claim.Text.Contains("Front intake", StringComparison.Ordinal)
            && claim.Text.Contains("GPU", StringComparison.Ordinal));
        Assert.Contains(gpuPath.Claims, claim =>
            claim.Title == "Bottom intake"
            && claim.Evidence == MetricEvidence.Measured
            && claim.Text.Contains("6.5 °C cooler", StringComparison.Ordinal));
        Assert.Contains(gpuPath.Claims, claim =>
            claim.Title == "Front intake"
            && claim.Evidence == MetricEvidence.Measured
            && claim.Text.Contains("4.2 °C cooler", StringComparison.Ordinal));

        ExplanationSection cpuPath = Section(report, ExplanationReportBuilder.PrimaryCpuPathHeading);
        Assert.Contains(cpuPath.Claims, claim =>
            claim.Evidence == MetricEvidence.Inferred
            && claim.Text.Contains("Rear exhaust", StringComparison.Ordinal)
            && claim.Text.Contains("Top exhaust", StringComparison.Ordinal)
            && claim.Text.Contains("CPU", StringComparison.Ordinal));
        Assert.Contains(cpuPath.Claims, claim =>
            claim.Evidence == MetricEvidence.Measured
            && claim.Title == "Rear exhaust"
            && claim.Text.Contains("4.5 °C cooler", StringComparison.Ordinal));

        ExplanationSection most = Section(report, ExplanationReportBuilder.MostEffectiveFanHeading);
        ExplanationClaim mostClaim = Assert.Single(most.Claims);
        Assert.Equal(MetricEvidence.Measured, mostClaim.Evidence);
        Assert.Equal("Bottom intake", mostClaim.Title);
        Assert.Contains("6.5 °C cooler on the GPU", mostClaim.Text, StringComparison.Ordinal);

        ExplanationSection diminishing = Section(report, ExplanationReportBuilder.DiminishingReturnsHeading);
        ExplanationClaim diminishingClaim = Assert.Single(diminishing.Claims);
        Assert.Equal(MetricEvidence.Measured, diminishingClaim.Evidence);
        Assert.Contains("Top exhaust", diminishingClaim.Text, StringComparison.Ordinal);
        Assert.Contains("1200 RPM", diminishingClaim.Text, StringComparison.Ordinal);

        ExplanationSection interaction = Section(report, ExplanationReportBuilder.DetectedInteractionHeading);
        Assert.Contains(interaction.Claims, claim =>
            claim.Evidence == MetricEvidence.Measured
            && claim.Text.Contains("1.6 °C", StringComparison.Ordinal)
            && claim.Text.Contains("more together", StringComparison.Ordinal));
        Assert.Contains(interaction.Claims, claim =>
            claim.Evidence == MetricEvidence.Inferred
            && claim.Text.Contains("guess", StringComparison.OrdinalIgnoreCase)
            && !claim.Text.Contains("°C", StringComparison.Ordinal)
            && !claim.Text.Contains("RPM", StringComparison.Ordinal)
            && !claim.Text.Contains("1.6", StringComparison.Ordinal));

        ExplanationSection recommended = Section(report, ExplanationReportBuilder.RecommendedPointHeading);
        Assert.NotEmpty(recommended.Claims);
        Assert.Contains(recommended.Claims, claim => claim.Title == "Front intake" && claim.Evidence == MetricEvidence.Modeled && claim.Text.Contains("% duty", StringComparison.Ordinal));
        Assert.Contains(recommended.Claims, claim =>
            claim.Title == "Noise"
            && claim.Evidence == MetricEvidence.Inferred
            && claim.Text.Contains("loudness", StringComparison.OrdinalIgnoreCase));

        ExplanationSection expected = Section(report, ExplanationReportBuilder.ExpectedResultHeading);
        Assert.Contains(expected.Claims, claim =>
            claim.Title == "GPU"
            && claim.Evidence == MetricEvidence.Modeled
            && claim.Text.Contains("°C cooler", StringComparison.Ordinal)
            && claim.Text.Contains("No baseline run is stored yet", StringComparison.Ordinal));
        Assert.Contains(expected.Claims, claim =>
            claim.Title == "CPU" && claim.Evidence == MetricEvidence.Modeled);
        Assert.Contains(expected.Claims, claim =>
            claim.Title == "Workload"
            && claim.Evidence == MetricEvidence.Modeled
            && claim.Text.Contains("CPU and GPU power", StringComparison.Ordinal)
            && claim.Text.Contains("not a second experiment tour", StringComparison.Ordinal)
            && claim.Text.Contains("not measured gaming truth", StringComparison.Ordinal));
    }

    [Fact]
    public void Expected_result_names_baseline_when_a_run_exists()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var baseline = new BaselineRun(
            Guid.NewGuid(),
            at,
            at,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: 22,
            GpuLoadAvailable: true,
            [],
            [
                new BaselineMetric(BaselineMetricNames.CpuRiseCelsius, 12.4, "°C", MetricEvidence.Measured),
            ]);
        ThermalModel model = AppMdModel(baseline);
        CoolingPolicy? policy = PolicyOptimizer.Recommend(model, AppMdReturns(), CoolingPreferences.Default);

        ExplanationReport report = ExplanationReportBuilder.Build(model, AppMdReturns(), policy);

        ExplanationSection expected = Section(report, ExplanationReportBuilder.ExpectedResultHeading);
        Assert.Contains(expected.Claims, claim =>
            claim.Title == "GPU"
            && claim.Evidence == MetricEvidence.Modeled
            && claim.Text.Contains("than baseline", StringComparison.Ordinal));
    }

    [Fact]
    public void Wander_section_reports_a_steady_gpu_and_drifting_cpu()
    {
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
        ThermalModel model = AppMdModel(
            new BaselineRun(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                BaselineRunStatus.Completed,
                AbortDetail: null,
                AmbientCelsius: null,
                GpuLoadAvailable: true,
                [],
                ReferenceStability.ToMetrics(assessment)));

        ExplanationReport report = ExplanationReportBuilder.Build(model, AppMdReturns(), policy: null);
        ExplanationSection wander = Section(report, ExplanationReportBuilder.WanderHeading);

        Assert.Contains(
            wander.Claims,
            claim => claim.Title == "GPU"
                && claim.Evidence == MetricEvidence.Measured
                && claim.Text.Contains("steady", StringComparison.Ordinal));
        Assert.Contains(
            wander.Claims,
            claim => claim.Title == "CPU"
                && claim.Evidence == MetricEvidence.Measured
                && claim.Text.Contains("unproven", StringComparison.Ordinal));
    }

    [Fact]
    public void Inferred_path_text_has_no_measured_numbers()
    {
        ExplanationReport report = ExplanationReportBuilder.Build(
            AppMdModel(),
            AppMdReturns(),
            PolicyOptimizer.Recommend(AppMdModel(), AppMdReturns(), CoolingPreferences.Default));

        IReadOnlyList<ExplanationClaim> inferred = report.Sections
            .SelectMany(static section => section.Claims)
            .Where(static claim => claim.Evidence == MetricEvidence.Inferred)
            .ToArray();

        Assert.NotEmpty(inferred);
        Assert.All(
            inferred,
            static claim =>
            {
                Assert.DoesNotContain("°C", claim.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("RPM", claim.Text, StringComparison.Ordinal);
                Assert.DoesNotContain('%', claim.Text);
            });
    }

    [Fact]
    public void Empty_inputs_keep_seven_unknown_sections()
    {
        ThermalModel model = ThermalModelFitter.Fit(Identity, baseline: null, fanTest: null, interaction: null);
        DiminishingReturnsReport returns = DiminishingReturnsAnalyzer.Analyze([]);

        ExplanationReport report = ExplanationReportBuilder.Build(model, returns, policy: null);

        Assert.Equal(9, report.Sections.Count);
        Assert.All(
            report.Sections.SelectMany(static section => section.Claims),
            static claim => Assert.Equal(MetricEvidence.Unknown, claim.Evidence));
        Assert.Equal(
            ExplanationReportBuilder.NeedWatchHolds,
            Section(report, ExplanationReportBuilder.WanderHeading).Claims[0].Text);
        Assert.Equal(
            ExplanationReportBuilder.NeedFanTests,
            Section(report, ExplanationReportBuilder.PrimaryGpuPathHeading).Claims[0].Text);
        Assert.Equal(
            ExplanationReportBuilder.NeedFanTests,
            Section(report, ExplanationReportBuilder.MostEffectiveFanHeading).Claims[0].Text);
        Assert.Equal(
            ExplanationReportBuilder.NeedSpeedCurve,
            Section(report, ExplanationReportBuilder.DiminishingReturnsHeading).Claims[0].Text);
        Assert.Equal(
            ExplanationReportBuilder.NeedInteractions,
            Section(report, ExplanationReportBuilder.DetectedInteractionHeading).Claims[0].Text);
        Assert.Equal(
            ExplanationReportBuilder.NeedPolicy,
            Section(report, ExplanationReportBuilder.RecommendedPointHeading).Claims[0].Text);
        Assert.Equal(
            ExplanationReportBuilder.NeedPolicy,
            Section(report, ExplanationReportBuilder.ExpectedResultHeading).Claims[0].Text);
    }

    [Fact]
    public void Skipped_pairs_and_cool_gpu_are_unknown_not_leftovers()
    {
        DateTimeOffset at = DateTimeOffset.Parse("2026-09-20T06:00:00Z");
        var fanTest = new FanTestRun(
            Guid.NewGuid(),
            at,
            at,
            FanTestRunStatus.Aborted,
            SafetyLimits.Describe(ThermalAbortReason.RateOfRise),
            GpuLoadAvailable: true,
            [],
            [
                new InfluenceEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Cpu,
                    2.4,
                    InfluenceEffect.Medium,
                    MetricEvidence.Measured,
                    40,
                    70,
                    800,
                    1200),
                new InfluenceEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Gpu,
                    DeltaCelsius: null,
                    Effect: null,
                    MetricEvidence.Unknown,
                    40,
                    DutyAfter: null,
                    800,
                    RpmAfter: null,
                    FanTestReasons.GpuHeatInsufficient),
            ],
            []);
        ThermalModel model = ThermalModelFitter.Fit(Identity, baseline: null, fanTest, interaction: null);
        DiminishingReturnsReport returns = DiminishingReturnsAnalyzer.Analyze([]);

        ExplanationReport report = ExplanationReportBuilder.Build(model, returns, policy: null);

        Assert.Equal(
            ExplanationReportBuilder.GpuHeatUnknown,
            Section(report, ExplanationReportBuilder.PrimaryGpuPathHeading).Claims[0].Text);
        Assert.Equal(
            ExplanationReportBuilder.PairsSkipped,
            Section(report, ExplanationReportBuilder.DetectedInteractionHeading).Claims[0].Text);
    }

    [Fact]
    public void Confirmation_miss_is_measured_versus_modeled()
    {
        ThermalModel model = AppMdModel();
        CoolingPolicy? policy = PolicyOptimizer.Recommend(model, AppMdReturns(), CoolingPreferences.Default);
        var confirmation = new PolicyConfirmation(78, 72, 74, 70, Missed: true, AddedAirflow: true);

        ExplanationReport report = ExplanationReportBuilder.Build(model, AppMdReturns(), policy, confirmation);

        ExplanationSection section = Section(report, ExplanationReportBuilder.ConfirmationHeading);
        Assert.Contains(section.Claims, claim =>
            claim.Title == "CPU"
            && claim.Evidence == MetricEvidence.Measured
            && claim.Text.Contains("78.0 °C", StringComparison.Ordinal)
            && claim.Text.Contains("74.0 °C", StringComparison.Ordinal));
        Assert.Contains(section.Claims, claim =>
            claim.Evidence == MetricEvidence.Measured
            && claim.Text.Contains("airflow was raised", StringComparison.Ordinal));
    }

    [Fact]
    public void Ambient_only_stale_is_inferred_without_numbers()
    {
        ThermalModel model = AppMdModel();
        CoolingPolicy? policy = PolicyOptimizer.Recommend(model, AppMdReturns(), CoolingPreferences.Default);
        var confirmation = new PolicyConfirmation(70, 65, 70, 65, Missed: false, AddedAirflow: false, AmbientCelsius: 27);
        DriftAssessment drift = DriftDetector.Assess(confirmation, 22, model.Influence);

        ExplanationReport report = ExplanationReportBuilder.Build(model, AppMdReturns(), policy, confirmation, drift);

        ExplanationSection section = Section(report, ExplanationReportBuilder.ConfirmationHeading);
        Assert.Contains(section.Claims, claim =>
            claim.Title == "Drift"
            && claim.Evidence == MetricEvidence.Inferred
            && claim.Text.Contains("room looks different", StringComparison.Ordinal)
            && !claim.Text.Contains("°C", StringComparison.Ordinal));
        Assert.Contains(section.Claims, claim =>
            claim.Title == "Stale"
            && claim.Evidence == MetricEvidence.Inferred
            && claim.Text.Contains("re-check", StringComparison.Ordinal));
    }

    [Fact]
    public void Tiny_residual_is_measured_no_leftover_without_inferred_note()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            FanTest(
            [
                Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Gpu, 1.5, InfluenceEffect.Low),
                Measured(FakeHardwareBackend.TopFanId, "Top exhaust", InfluenceTarget.Gpu, 0.7, InfluenceEffect.Low),
            ]),
            Interaction(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                FakeHardwareBackend.TopFanId,
                "Top exhaust",
                InfluenceTarget.Gpu,
                1.5,
                0.7,
                2.3,
                0.1,
                InferredNote: null));

        ExplanationReport report = ExplanationReportBuilder.Build(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            PolicyOptimizer.Recommend(model, DiminishingReturnsAnalyzer.Analyze([]), CoolingPreferences.Default));

        ExplanationSection interaction = Section(report, ExplanationReportBuilder.DetectedInteractionHeading);
        ExplanationClaim leftover = Assert.Single(interaction.Claims);
        Assert.Equal(MetricEvidence.Measured, leftover.Evidence);
        Assert.Equal(ExplanationReportBuilder.NoLeftover, leftover.Text);
        Assert.DoesNotContain(interaction.Claims, claim => claim.Evidence == MetricEvidence.Inferred);
    }

    [Fact]
    public void Claim_constructor_rejects_blank_text()
    {
        Assert.Throws<ArgumentException>(() =>
            new ExplanationClaim("Path", " ", MetricEvidence.Inferred));
    }

    private static ExplanationSection Section(ExplanationReport report, string heading) =>
        Assert.Single(report.Sections, section => section.Heading == heading);

    private static ThermalModel AppMdModel(BaselineRun? baseline = null) =>
        ThermalModelFitter.Fit(
            Identity,
            baseline,
            FanTest(
            [
                Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Gpu, 4.2, InfluenceEffect.High),
                Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Cpu, 1.0, InfluenceEffect.Low),
                Measured(BottomFanId, "Bottom intake", InfluenceTarget.Gpu, 6.5, InfluenceEffect.VeryHigh),
                Measured(BottomFanId, "Bottom intake", InfluenceTarget.Cpu, 0.4, InfluenceEffect.None),
                Measured(FakeHardwareBackend.RearFanId, "Rear exhaust", InfluenceTarget.Cpu, 4.5, InfluenceEffect.High),
                Measured(FakeHardwareBackend.RearFanId, "Rear exhaust", InfluenceTarget.Gpu, 1.2, InfluenceEffect.Low),
                Measured(FakeHardwareBackend.TopFanId, "Top exhaust", InfluenceTarget.Cpu, 3.2, InfluenceEffect.High),
                Measured(FakeHardwareBackend.TopFanId, "Top exhaust", InfluenceTarget.Gpu, 0.8, InfluenceEffect.Low),
            ]),
            Interaction(
                BottomFanId,
                "Bottom intake",
                FakeHardwareBackend.RearFanId,
                "Rear exhaust",
                InfluenceTarget.Gpu,
                6.5,
                1.2,
                9.3,
                1.6,
                InteractionBuilder.InferNote(1.6, InfluenceTarget.Gpu)));

    private static DiminishingReturnsReport AppMdReturns() =>
        DiminishingReturnsAnalyzer.Analyze(
        [
            new FanSpeedCurve(
                FakeHardwareBackend.TopFanId,
                "Top exhaust",
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

    private static FanTestRun FanTest(IReadOnlyList<InfluenceEntry> influence) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            influence,
            []);

    private static InteractionRun Interaction(
        string firstId,
        string firstName,
        string secondId,
        string secondName,
        InfluenceTarget target,
        double firstDelta,
        double secondDelta,
        double combined,
        double residual,
        string? InferredNote) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                new InteractionEntry(
                    firstId,
                    firstName,
                    secondId,
                    secondName,
                    target,
                    firstDelta,
                    secondDelta,
                    combined,
                    residual,
                    MetricEvidence.Measured,
                    InferredNote),
            ],
            []);

    private static InfluenceEntry Measured(
        string id,
        string name,
        InfluenceTarget target,
        double delta,
        InfluenceEffect effect) =>
        new(
            id,
            name,
            target,
            delta,
            effect,
            MetricEvidence.Measured,
            DutyBefore: 35,
            DutyAfter: 60,
            RpmBefore: 490,
            RpmAfter: 840);
}
