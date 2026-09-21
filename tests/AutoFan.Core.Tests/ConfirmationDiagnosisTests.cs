using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class ConfirmationDiagnosisTests
{
    private static readonly HardwareIdentity Identity = new(
        "This PC",
        "CPU",
        "GPU",
        "Board");

    [Fact]
    public void Explain_matches_predict_for_a_measured_pair()
    {
        ThermalModel model = ThreeFanModel(
            front: 1.5,
            hub: 0.7,
            gpu: 0.4,
            frontHub: 1.6,
            frontGpu: 0,
            hubGpu: 0,
            includeFrontGpuPair: false,
            includeHubGpuPair: false);

        string[] groups =
        [
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.TopFanId,
        ];
        ThermalPrediction predicted = model.Predict(groups, InfluenceTarget.Gpu);
        PredictionBreakdown breakdown = model.Explain(groups, InfluenceTarget.Gpu);

        Assert.Equal(3.8, predicted.DeltaCelsius ?? 0, 1);
        Assert.Equal(predicted.DeltaCelsius, breakdown.TotalDeltaCelsius);
        Assert.Equal(PredictionReason.MeasuredPair, breakdown.Reason);
        Assert.Equal(2, breakdown.Fans.Count);
        Assert.Single(breakdown.Pairs);
        Assert.Equal(1.6, breakdown.PairTotalCelsius, 1);
        Assert.Equal("none", PredictionBreakdown.NoOtherCorrections);
    }

    [Fact]
    public void Three_fans_use_singles_only_not_all_pair_leftovers()
    {
        ThermalModel model = BelowIdleModel();
        string[] groups =
        [
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.TopFanId,
            FakeHardwareBackend.GpuFanId,
        ];
        PredictionBreakdown breakdown = model.Explain(groups, InfluenceTarget.Gpu);

        Assert.Equal(21.2, breakdown.TotalDeltaCelsius ?? 0, 1);
        Assert.Equal(3, breakdown.Fans.Count);
        Assert.Empty(breakdown.Pairs);
        Assert.Equal(PredictionReason.AdditiveEstimate, breakdown.Reason);
    }

    [Fact]
    public void Explain_omits_a_group_without_gpu_delta()
    {
        ThermalModel model = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            FanTest(
            [
                Measured(FakeHardwareBackend.FrontFanId, "CPU Fan", InfluenceTarget.Cpu, 4.0),
                Measured(FakeHardwareBackend.FrontFanId, "CPU Fan", InfluenceTarget.Gpu, 8.4),
                Measured(FakeHardwareBackend.TopFanId, "System Fan #1", InfluenceTarget.Cpu, 3.0),
            ]),
            interaction: null);

        PredictionBreakdown breakdown = model.Explain(
            [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.TopFanId],
            InfluenceTarget.Gpu);

        Assert.Equal(8.4, breakdown.TotalDeltaCelsius ?? 0, 1);
        Assert.Equal(MetricEvidence.Modeled, breakdown.Evidence);
        Assert.Equal(PredictionReason.MeasuredSingle, breakdown.Reason);
        Assert.Single(breakdown.Fans);
        Assert.Empty(breakdown.Pairs);
    }

    [Fact]
    public void Mean_idle_gpu_uses_idle_baseline_samples()
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
                new BaselineSample(at, BaselinePhase.Idle, GpuSnapshot(46.0)),
                new BaselineSample(at, BaselinePhase.Idle, GpuSnapshot(46.4)),
                new BaselineSample(at, BaselinePhase.Low, GpuSnapshot(59.0)),
            ],
            []);

        Assert.Equal(46.2, ConfirmationDiagnosis.MeanPhaseTemperature(
            baseline,
            BaselinePhase.Idle,
            SensorKind.GpuTemperature) ?? 0,
            1);
    }

    private static ThermalModel BelowIdleModel() =>
        ThreeFanModel(
            front: 8.4,
            hub: 7.2,
            gpu: 5.6,
            frontHub: 2.0,
            frontGpu: 1.5,
            hubGpu: 1.5,
            includeFrontGpuPair: true,
            includeHubGpuPair: true);

    private static ThermalModel ThreeFanModel(
        double front,
        double hub,
        double gpu,
        double frontHub,
        double frontGpu,
        double hubGpu,
        bool includeFrontGpuPair,
        bool includeHubGpuPair)
    {
        var effects = new List<InteractionEntry>
        {
            Pair(
                FakeHardwareBackend.FrontFanId,
                "CPU Fan",
                FakeHardwareBackend.TopFanId,
                "System Fan #1",
                front,
                hub,
                front + hub + frontHub,
                frontHub),
        };
        if (includeFrontGpuPair)
        {
            effects.Add(Pair(
                FakeHardwareBackend.FrontFanId,
                "CPU Fan",
                FakeHardwareBackend.GpuFanId,
                "GPU Fan 1",
                front,
                gpu,
                front + gpu + frontGpu,
                frontGpu));
        }

        if (includeHubGpuPair)
        {
            effects.Add(Pair(
                FakeHardwareBackend.TopFanId,
                "System Fan #1",
                FakeHardwareBackend.GpuFanId,
                "GPU Fan 1",
                hub,
                gpu,
                hub + gpu + hubGpu,
                hubGpu));
        }

        return ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            FanTest(
            [
                Measured(FakeHardwareBackend.FrontFanId, "CPU Fan", InfluenceTarget.Gpu, front),
                Measured(FakeHardwareBackend.TopFanId, "System Fan #1", InfluenceTarget.Gpu, hub),
                Measured(FakeHardwareBackend.GpuFanId, "GPU Fan 1", InfluenceTarget.Gpu, gpu),
            ]),
            Interaction(effects));
    }

    private static InfluenceEntry Measured(string id, string name, InfluenceTarget target, double delta) =>
        new(
            id,
            name,
            target,
            delta,
            InfluenceMapBuilder.Classify(delta),
            MetricEvidence.Measured,
            40,
            100,
            500,
            1200);

    private static InteractionEntry Pair(
        string firstId,
        string firstName,
        string secondId,
        string secondName,
        double first,
        double second,
        double combined,
        double residual) =>
        new(
            firstId,
            firstName,
            secondId,
            secondName,
            InfluenceTarget.Gpu,
            first,
            second,
            combined,
            residual,
            MetricEvidence.Measured,
            InferredNote: null);

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

    private static HardwareSnapshot GpuSnapshot(double gpuCelsius) =>
        new(
            DateTimeOffset.UtcNow,
            [
                new SensorReading("gpu-core", "GPU Core", SensorKind.GpuTemperature, gpuCelsius, "Â°C"),
            ],
            [],
            IsDemoHardware: true);
}
