using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class ScreenOrdererTests
{
    private static readonly HardwareIdentity Identity = new(
        "This PC",
        "Intel Core i7-13700K",
        "NVIDIA GeForce RTX 4070",
        "ASUS ROG STRIX Z790-E");

    [Fact]
    public void FromName_maps_known_tokens_and_leaves_numbered_headers_unknown()
    {
        Assert.Equal(CaseZone.CpuHeader, CaseZoneMatcher.FromName("CPU Fan"));
        Assert.Equal(CaseZone.CpuHeader, CaseZoneMatcher.FromName("CPU_FAN"));
        Assert.Equal(CaseZone.Front, CaseZoneMatcher.FromName("Front intake"));
        Assert.Equal(CaseZone.Bottom, CaseZoneMatcher.FromName("Bottom intake"));
        Assert.Equal(CaseZone.Rear, CaseZoneMatcher.FromName("Rear exhaust"));
        Assert.Equal(CaseZone.Top, CaseZoneMatcher.FromName("Top exhaust"));
        Assert.Equal(CaseZone.Unknown, CaseZoneMatcher.FromName("Fan #1"));
        Assert.Equal(CaseZone.Unknown, CaseZoneMatcher.FromName("SYS_FAN"));
        Assert.Equal(CaseZone.Unknown, CaseZoneMatcher.FromName("CHA_FAN1"));
        Assert.Equal(CaseZone.Unknown, CaseZoneMatcher.FromName(null));
    }

    [Fact]
    public void Order_puts_front_before_rear_before_unknown_and_never_drops_a_group()
    {
        FanGroup rear = Group("rear", "Rear exhaust");
        FanGroup unknown = Group("sys", "Fan #1");
        FanGroup front = Group("front", "Front intake");

        IReadOnlyList<FanGroup> frontFirst = ScreenOrderer.Order(
            [front, rear, unknown],
            CasePriorCatalog.GenericMidTower);
        IReadOnlyList<FanGroup> rearFirst = ScreenOrderer.Order(
            [rear, unknown, front],
            CasePriorCatalog.GenericMidTower);

        Assert.Equal(["front", "rear", "sys"], frontFirst.Select(group => group.Id).ToArray());
        Assert.Equal(["front", "rear", "sys"], rearFirst.Select(group => group.Id).ToArray());
        Assert.Equal(3, rearFirst.Count);
    }

    [Fact]
    public void Order_keeps_same_zone_in_discovery_order()
    {
        FanGroup firstFront = Group("front-a", "Front intake A");
        FanGroup secondFront = Group("front-b", "Front intake B");
        FanGroup rear = Group("rear", "Rear exhaust");

        IReadOnlyList<FanGroup> ordered = ScreenOrderer.Order(
            [secondFront, rear, firstFront],
            CasePriorCatalog.GenericMidTower);

        Assert.Equal(["front-b", "front-a", "rear"], ordered.Select(group => group.Id).ToArray());
    }

    [Fact]
    public void Hub_header_stays_one_screen_group()
    {
        // Four physical fans on this header are still one writable group.
        FanGroup hub = Group("sys-hub", "SYS_FAN");

        IReadOnlyList<FanGroup> ordered = ScreenOrderer.Order(
            [hub],
            CasePriorCatalog.GenericMidTower);

        Assert.Equal(["sys-hub"], ordered.Select(group => group.Id).ToArray());
    }

    [Fact]
    public void Same_measurements_and_different_priors_do_not_change_the_fitted_model()
    {
        FanGroup front = Group("front", "Front intake");
        FanGroup rear = Group("rear", "Rear exhaust");
        FanGroup unknown = Group("sys", "Fan #1");
        CasePrior gpuFirst = CasePriorCatalog.GenericMidTower;
        CasePrior cpuFirst = new(
            "cpu-first",
            [
                new CasePathGuess(CaseZone.Front, InfluenceTarget.Cpu),
                new CasePathGuess(CaseZone.Rear, InfluenceTarget.Gpu),
            ]);

        IReadOnlyList<string> gpuOrder = ScreenOrderer.Order([rear, unknown, front], gpuFirst)
            .Select(group => group.Id)
            .ToArray();
        IReadOnlyList<string> cpuOrder = ScreenOrderer.Order([rear, unknown, front], cpuFirst)
            .Select(group => group.Id)
            .ToArray();

        Assert.Equal(["front", "rear", "sys"], gpuOrder);
        Assert.Equal(["rear", "front", "sys"], cpuOrder);

        DateTimeOffset start = new(2026, 9, 19, 19, 0, 0, TimeSpan.Zero);
        IReadOnlyList<FanTestSample> gpuSamples = SamplesInOrder(start, gpuOrder);
        IReadOnlyList<FanTestSample> cpuSamples = SamplesInOrder(start, cpuOrder);
        IReadOnlyList<InfluenceEntry> gpuInfluence = InfluenceMapBuilder.Build(gpuSamples);
        IReadOnlyList<InfluenceEntry> cpuInfluence = InfluenceMapBuilder.Build(cpuSamples);

        AssertEffectsMatch(gpuInfluence, cpuInfluence);

        ThermalModel gpuModel = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            FanTest(gpuInfluence),
            interaction: null);
        ThermalModel cpuModel = ThermalModelFitter.Fit(
            Identity,
            baseline: null,
            FanTest(cpuInfluence),
            interaction: null);

        ThermalPrediction gpuFromGpu = gpuModel.Predict(["front"], InfluenceTarget.Gpu);
        ThermalPrediction gpuFromCpu = cpuModel.Predict(["front"], InfluenceTarget.Gpu);
        ThermalPrediction cpuFromGpu = gpuModel.Predict(["rear"], InfluenceTarget.Cpu);
        ThermalPrediction cpuFromCpu = cpuModel.Predict(["rear"], InfluenceTarget.Cpu);

        Assert.Equal(gpuFromGpu.DeltaCelsius, gpuFromCpu.DeltaCelsius);
        Assert.Equal(cpuFromGpu.DeltaCelsius, cpuFromCpu.DeltaCelsius);
        Assert.Equal(gpuFromGpu.Evidence, gpuFromCpu.Evidence);
        Assert.Equal(gpuModel.Confidence, cpuModel.Confidence);
    }

    private static void AssertEffectsMatch(
        IReadOnlyList<InfluenceEntry> first,
        IReadOnlyList<InfluenceEntry> second)
    {
        Assert.Equal(first.Count, second.Count);
        foreach (InfluenceEntry entry in first)
        {
            InfluenceEntry match = second.Single(
                item => item.FanGroupId == entry.FanGroupId && item.Target == entry.Target);
            Assert.Equal(entry.Effect, match.Effect);
            Assert.Equal(entry.Evidence, match.Evidence);
            Assert.Equal(entry.DeltaCelsius, match.DeltaCelsius);
        }
    }

    private static IReadOnlyList<FanTestSample> SamplesInOrder(
        DateTimeOffset start,
        IReadOnlyList<string> groupIds)
    {
        var samples = new List<FanTestSample>();
        for (int index = 0; index < groupIds.Count; index++)
        {
            DateTimeOffset at = start.AddMinutes(index * 2);
            samples.AddRange(groupIds[index] switch
            {
                "front" => GroupSamples(at, "front", "Front intake", cpuPerturb: 69.8, gpuPerturb: 65.5),
                "rear" => GroupSamples(at, "rear", "Rear exhaust", cpuPerturb: 66.4, gpuPerturb: 71.2),
                _ => GroupSamples(at, "sys", "Fan #1", cpuPerturb: 69.9, gpuPerturb: 71.8),
            });
        }

        return samples;
    }

    private static IReadOnlyList<FanTestSample> GroupSamples(
        DateTimeOffset start,
        string id,
        string name,
        double cpuPerturb,
        double gpuPerturb) =>
    [
        Sample(start, id, name, FanTestStage.Reference, 70, 72),
        Sample(start.AddSeconds(45), id, name, FanTestStage.Perturb, cpuPerturb, gpuPerturb),
        Sample(start.AddSeconds(46), id, name, FanTestStage.Perturb, cpuPerturb, gpuPerturb),
        Sample(start.AddSeconds(47), id, name, FanTestStage.Perturb, cpuPerturb, gpuPerturb),
    ];

    private static FanTestSample Sample(
        DateTimeOffset at,
        string id,
        string name,
        FanTestStage stage,
        double cpu,
        double gpu)
    {
        int duty = stage == FanTestStage.Reference ? 40 : 70;
        double rpm = stage == FanTestStage.Reference ? 800 : 1400;
        var snapshot = new HardwareSnapshot(
            at,
            [
                new SensorReading("cpu-temp", "CPU", SensorKind.CpuTemperature, cpu, "°C"),
                new SensorReading("gpu-temp", "GPU", SensorKind.GpuTemperature, gpu, "°C"),
            ],
            [new FanGroup(id, name, duty, rpm, "Demo controller", IsControllable: true)],
            IsDemoHardware: true);
        return new FanTestSample(at, id, name, stage, snapshot);
    }

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

    private static FanGroup Group(string id, string name) =>
        new(id, name, 40, 1000, "Demo controller", IsControllable: true);
}
