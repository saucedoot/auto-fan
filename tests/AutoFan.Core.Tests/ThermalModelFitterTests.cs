using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class ThermalModelFitterTests
{
    private static readonly HardwareIdentity Identity = new(
        "ASUS ROG STRIX Z790-E",
        "Intel Core i7-13700K",
        "NVIDIA GeForce RTX 4070",
        "ASUS ROG STRIX Z790-E");

    [Fact]
    public void Predict_uses_measured_pair_residual_not_the_sum_of_singles()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            TwoGroupFanTest(FanTestRunStatus.Completed),
            PairInteraction());

        ThermalPrediction gpu = model.Predict(
            [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.TopFanId],
            InfluenceTarget.Gpu);

        Assert.Equal(ModelConfidence.Medium, model.Confidence);
        Assert.Equal(3.8, gpu.DeltaCelsius ?? 0, 1);
        Assert.Equal(MetricEvidence.Modeled, gpu.Evidence);
        Assert.Equal(PredictionReason.MeasuredPair, gpu.Reason);
        Assert.NotEqual(2.2, gpu.DeltaCelsius);
    }

    [Fact]
    public void Missing_fan_test_is_none_and_never_high()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            fanTest: null,
            interaction: null);

        ThermalPrediction prediction = model.Predict([FakeHardwareBackend.FrontFanId], InfluenceTarget.Cpu);

        Assert.Equal(ModelConfidence.None, model.Confidence);
        Assert.NotEqual(ModelConfidence.High, model.Confidence);
        Assert.Null(prediction.DeltaCelsius);
        Assert.Equal(MetricEvidence.Unknown, prediction.Evidence);
        Assert.Equal(PredictionReason.Unknown, prediction.Reason);
        Assert.Contains("Run fan tests first", model.ConfidenceReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_only_influence_is_none()
    {
        var run = FanTest(
            FanTestRunStatus.Completed,
            [
                new InfluenceEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Cpu,
                    DeltaCelsius: null,
                    Effect: null,
                    MetricEvidence.Unknown,
                    35,
                    60,
                    490,
                    840),
            ]);

        ThermalModel model = ThermalModelFitter.Fit(Identity, baseline: null, run, interaction: null);

        Assert.Equal(ModelConfidence.None, model.Confidence);
        Assert.NotEqual(ModelConfidence.High, model.Confidence);
    }

    [Fact]
    public void One_measured_group_is_not_high()
    {
        var run = FanTest(
            FanTestRunStatus.Completed,
            [
                Measured(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Gpu,
                    1.5,
                    InfluenceEffect.Low),
            ]);

        ThermalModel model = ThermalModelFitter.Fit(Identity, baseline: null, run, interaction: null);
        ThermalPrediction gpu = model.Predict([FakeHardwareBackend.FrontFanId], InfluenceTarget.Gpu);

        Assert.Equal(ModelConfidence.Low, model.Confidence);
        Assert.NotEqual(ModelConfidence.High, model.Confidence);
        Assert.Equal(1.5, gpu.DeltaCelsius);
        Assert.Equal(MetricEvidence.Modeled, gpu.Evidence);
        Assert.Equal(PredictionReason.MeasuredSingle, gpu.Reason);
    }

    [Fact]
    public void Aborted_fan_test_is_not_high_even_with_two_groups()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            TwoGroupFanTest(FanTestRunStatus.Aborted),
            PairInteraction());

        Assert.Equal(ModelConfidence.Low, model.Confidence);
        Assert.NotEqual(ModelConfidence.High, model.Confidence);
        Assert.Contains("did not finish", model.ConfidenceReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Skipped_pairs_do_not_apply_a_stale_leftover()
    {
        ThermalModel withStalePair = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            TwoGroupFanTest(FanTestRunStatus.Aborted),
            PairInteraction());
        ThermalModel skipped = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            TwoGroupFanTest(FanTestRunStatus.Aborted),
            interaction: null);

        ThermalPrediction staleGpu = withStalePair.Predict(
            [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.TopFanId],
            InfluenceTarget.Gpu);
        ThermalPrediction skippedGpu = skipped.Predict(
            [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.TopFanId],
            InfluenceTarget.Gpu);

        Assert.Equal(3.8, staleGpu.DeltaCelsius ?? 0, 1);
        Assert.Equal(2.2, skippedGpu.DeltaCelsius);
        Assert.Equal(PredictionReason.AdditiveEstimate, skippedGpu.Reason);
    }

    [Fact]
    public void Two_groups_without_interactions_are_medium_and_additive()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            TwoGroupFanTest(FanTestRunStatus.Completed),
            interaction: null);

        ThermalPrediction gpu = model.Predict(
            [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.TopFanId],
            InfluenceTarget.Gpu);

        Assert.Equal(ModelConfidence.Medium, model.Confidence);
        Assert.NotEqual(ModelConfidence.High, model.Confidence);
        Assert.Equal(2.2, gpu.DeltaCelsius);
        Assert.Equal(MetricEvidence.Modeled, gpu.Evidence);
        Assert.Equal(PredictionReason.AdditiveEstimate, gpu.Reason);
    }

    [Fact]
    public void Adding_a_measured_residual_raises_confidence_to_high()
    {
        ThermalModel withoutPair = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            TwoGroupFanTest(FanTestRunStatus.Completed),
            interaction: null);
        ThermalModel withPair = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            TwoGroupFanTest(FanTestRunStatus.Completed),
            PairInteraction());

        Assert.Equal(ModelConfidence.Medium, withoutPair.Confidence);
        Assert.Equal(ModelConfidence.Medium, withPair.Confidence);
        Assert.Contains("lighter heat", withPair.ConfidenceReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_carries_this_machine_names_not_a_case_sku()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            TwoGroupFanTest(FanTestRunStatus.Completed),
            interaction: null);

        Assert.Equal("Intel Core i7-13700K", model.CpuName);
        Assert.Equal("NVIDIA GeForce RTX 4070", model.GpuName);
        Assert.Equal("ASUS ROG STRIX Z790-E", model.MotherboardName);
        Assert.DoesNotContain("Lian Li", model.CpuName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("case", model.MotherboardName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_identity_names_are_unknown_not_a_case()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            HardwareIdentity.Unknown,
            baseline: null,
            fanTest: null,
            interaction: null);

        Assert.Equal("unknown", model.CpuName);
        Assert.Equal("unknown", model.GpuName);
        Assert.Equal("unknown", model.MotherboardName);
    }

    [Fact]
    public void Baseline_is_kept_as_context_and_does_not_raise_confidence()
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;
        var baseline = new BaselineRun(
            Guid.NewGuid(),
            at,
            at,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: 22.5,
            GpuLoadAvailable: true,
            [],
            [
                new BaselineMetric(BaselineMetricNames.CpuRiseCelsius, 12.4, "°C", MetricEvidence.Measured),
            ]);

        ThermalModel empty = ThermalModelFitter.Fit(Identity, baseline, fanTest: null, interaction: null);
        ThermalModel medium = ThermalModelFitter.Fit(
            Identity,
            baseline,
            TwoGroupFanTest(FanTestRunStatus.Completed),
            interaction: null);

        Assert.Equal(ModelConfidence.None, empty.Confidence);
        Assert.Equal(ModelConfidence.Medium, medium.Confidence);
        Assert.Same(baseline, medium.Baseline);
    }

    [Fact]
    public void Relationship_text_uses_measured_strength_words()
    {
        var entry = Measured(
            FakeHardwareBackend.FrontFanId,
            "Bottom intake",
            InfluenceTarget.Gpu,
            6.5,
            InfluenceEffect.VeryHigh);

        Assert.Equal("Bottom intake very strongly affects GPU", ThermalModelFitter.DescribeRelationship(entry));
        Assert.Equal("strongly affects", ThermalModelFitter.DescribeEffect(InfluenceEffect.High));
    }

    [Fact]
    public void Three_groups_use_singles_only_not_every_pair_leftover()
    {
        var fanTest = FanTest(
            FanTestRunStatus.Completed,
            [
                Measured(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Gpu,
                    1.5,
                    InfluenceEffect.Low),
                Measured(
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Gpu,
                    0.7,
                    InfluenceEffect.Low),
                Measured(
                    FakeHardwareBackend.RearFanId,
                    "Rear exhaust",
                    InfluenceTarget.Gpu,
                    0.4,
                    InfluenceEffect.None),
            ]);
        var interaction = Interaction(
            [
                new InteractionEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Gpu,
                    1.5,
                    0.7,
                    3.8,
                    1.6,
                    MetricEvidence.Measured,
                    InferredNote: null),
            ]);

        ThermalModel model = ThermalModelFitter.Fit(Identity, baseline: null, fanTest, interaction);
        ThermalPrediction gpu = model.Predict(
            [
                FakeHardwareBackend.FrontFanId,
                FakeHardwareBackend.TopFanId,
                FakeHardwareBackend.RearFanId,
            ],
            InfluenceTarget.Gpu);

        Assert.Equal(2.6, gpu.DeltaCelsius ?? 0, 1);
        Assert.Equal(PredictionReason.AdditiveEstimate, gpu.Reason);
        Assert.Equal(MetricEvidence.Modeled, gpu.Evidence);
    }

    private static FanTestRun TwoGroupFanTest(FanTestRunStatus status) =>
        FanTest(
            status,
            [
                Measured(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    InfluenceTarget.Gpu,
                    1.5,
                    InfluenceEffect.Low),
                Measured(
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Gpu,
                    0.7,
                    InfluenceEffect.Low),
            ]);

    private static InteractionRun PairInteraction() =>
        Interaction(
            [
                new InteractionEntry(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    FakeHardwareBackend.TopFanId,
                    "Top exhaust",
                    InfluenceTarget.Gpu,
                    1.5,
                    0.7,
                    3.8,
                    1.6,
                    MetricEvidence.Measured,
                    InferredNote: null),
            ]);

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
            35,
            60,
            490,
            840);

    private static FanTestRun FanTest(FanTestRunStatus status, IReadOnlyList<InfluenceEntry> influence) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            status,
            AbortDetail: status == FanTestRunStatus.Aborted ? "CPU 90 °C" : null,
            GpuLoadAvailable: true,
            [],
            influence,
            []);

    private static InteractionRun Interaction(IReadOnlyList<InteractionEntry> effects) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            effects,
            []);
}
