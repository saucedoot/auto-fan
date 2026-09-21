using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class InfluenceMapBuilderTests
{
    [Fact]
    public void Build_bins_measured_effects_and_keeps_none_instead_of_omitting()
    {
        DateTimeOffset start = new(2026, 9, 19, 17, 0, 0, TimeSpan.Zero);
        FanTestSample[] samples =
        [
            .. Group(
                start,
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                cpuReference: 70,
                gpuReference: 72,
                vrmReference: 60,
                caseReference: 45,
                cpuPerturb: 69.8,
                gpuPerturb: 65.5,
                vrmPerturb: 59.0,
                casePerturb: 43.0),
            .. Group(
                start.AddMinutes(2),
                FakeHardwareBackend.RearFanId,
                "Rear exhaust",
                cpuReference: 70,
                gpuReference: 72,
                vrmReference: 60,
                caseReference: 45,
                cpuPerturb: 66.4,
                gpuPerturb: 71.2,
                vrmPerturb: 58.8,
                casePerturb: 43.8),
            .. Group(
                start.AddMinutes(4),
                FakeHardwareBackend.TopFanId,
                "Top exhaust",
                cpuReference: 70,
                gpuReference: 72,
                vrmReference: 60,
                caseReference: 45,
                cpuPerturb: 69.7,
                gpuPerturb: 71.9,
                vrmPerturb: 59.8,
                casePerturb: 44.8),
        ];

        IReadOnlyList<InfluenceEntry> map = InfluenceMapBuilder.Build(samples);

        Assert.Equal(12, map.Count);
        AssertEntry(map, FakeHardwareBackend.FrontFanId, InfluenceTarget.Cpu, InfluenceEffect.None);
        AssertEntry(map, FakeHardwareBackend.FrontFanId, InfluenceTarget.Gpu, InfluenceEffect.VeryHigh, 6.5);
        AssertEntry(map, FakeHardwareBackend.FrontFanId, InfluenceTarget.Vrm, InfluenceEffect.Low, 1.0);
        AssertEntry(map, FakeHardwareBackend.FrontFanId, InfluenceTarget.Case, InfluenceEffect.Medium, 2.0);
        AssertEntry(map, FakeHardwareBackend.RearFanId, InfluenceTarget.Cpu, InfluenceEffect.High, 3.6);
        AssertEntry(map, FakeHardwareBackend.RearFanId, InfluenceTarget.Gpu, InfluenceEffect.Low, 0.8);
        AssertEntry(map, FakeHardwareBackend.TopFanId, InfluenceTarget.Gpu, InfluenceEffect.None);
        Assert.Contains(
            map,
            entry => entry.FanGroupId == FakeHardwareBackend.TopFanId
                && entry.Target == InfluenceTarget.Gpu
                && entry.Effect == InfluenceEffect.None
                && entry.Evidence == MetricEvidence.Measured);
    }

    [Fact]
    public void WithoutGpuTargets_keeps_cpu_measured_and_marks_gpu_unknown()
    {
        DateTimeOffset start = new(2026, 9, 19, 17, 0, 0, TimeSpan.Zero);
        IReadOnlyList<InfluenceEntry> map = InfluenceMapBuilder.WithoutGpuTargets(
            InfluenceMapBuilder.Build(
            [
                .. Group(
                    start,
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    cpuReference: 70,
                    gpuReference: 72,
                    vrmReference: 60,
                    caseReference: 45,
                    cpuPerturb: 66,
                    gpuPerturb: 65.5,
                    vrmPerturb: 59.0,
                    casePerturb: 43.0),
            ]),
            FanTestReasons.GpuHeatInsufficient);

        InfluenceEntry cpu = map.Single(
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId && entry.Target == InfluenceTarget.Cpu);
        InfluenceEntry gpu = map.Single(
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId && entry.Target == InfluenceTarget.Gpu);
        Assert.Equal(MetricEvidence.Measured, cpu.Evidence);
        Assert.Equal(MetricEvidence.Unknown, gpu.Evidence);
        Assert.Null(gpu.DeltaCelsius);
        Assert.Equal(FanTestReasons.GpuHeatInsufficient, gpu.SkipReason);
    }

    [Fact]
    public void Build_records_missing_case_sensor_as_unknown()
    {
        DateTimeOffset start = new(2026, 9, 19, 17, 0, 0, TimeSpan.Zero);
        FanTestSample[] samples =
        [
            Sample(start, FakeHardwareBackend.FrontFanId, "Front intake", FanTestStage.Reference, 70, 72, 60, caseTemp: null),
            .. PerturbCopies(start.AddSeconds(45), FakeHardwareBackend.FrontFanId, "Front intake", 68, 70, 58, caseTemp: null),
        ];

        IReadOnlyList<InfluenceEntry> map = InfluenceMapBuilder.Build(samples);

        InfluenceEntry caseEntry = map.Single(
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId && entry.Target == InfluenceTarget.Case);
        Assert.Equal(MetricEvidence.Unknown, caseEntry.Evidence);
        Assert.Null(caseEntry.DeltaCelsius);
        Assert.Null(caseEntry.Effect);
        Assert.Contains(
            map,
            entry => entry.Target == InfluenceTarget.Cpu
                && entry.Evidence == MetricEvidence.Measured
                && entry.Effect == InfluenceEffect.Medium);
    }

    [Fact]
    public void UnknownForGroup_emits_all_four_targets()
    {
        IReadOnlyList<InfluenceEntry> entries = InfluenceMapBuilder.UnknownForGroup(
            FakeHardwareBackend.TopFanId,
            "Top exhaust",
            FanTestReasons.AlreadyAtMaxDuty,
            dutyBefore: 100,
            rpmBefore: 1100);

        Assert.Equal(4, entries.Count);
        Assert.All(
            entries,
            entry =>
            {
                Assert.Equal(MetricEvidence.Unknown, entry.Evidence);
                Assert.Null(entry.DeltaCelsius);
                Assert.Equal(FanTestReasons.AlreadyAtMaxDuty, entry.SkipReason);
            });
        Assert.Equal(
            [InfluenceTarget.Cpu, InfluenceTarget.Gpu, InfluenceTarget.Vrm, InfluenceTarget.Case],
            entries.Select(entry => entry.Target).ToArray());
    }

    [Theory]
    [InlineData(0.2, InfluenceEffect.None)]
    [InlineData(0.5, InfluenceEffect.Low)]
    [InlineData(1.4, InfluenceEffect.Low)]
    [InlineData(1.5, InfluenceEffect.Medium)]
    [InlineData(2.9, InfluenceEffect.Medium)]
    [InlineData(3.0, InfluenceEffect.High)]
    [InlineData(4.9, InfluenceEffect.High)]
    [InlineData(5.0, InfluenceEffect.VeryHigh)]
    [InlineData(-6.2, InfluenceEffect.VeryHigh)]
    public void Classify_uses_absolute_delta(double delta, InfluenceEffect expected)
    {
        Assert.Equal(expected, InfluenceMapBuilder.Classify(delta));
    }

    private static void AssertEntry(
        IReadOnlyList<InfluenceEntry> map,
        string groupId,
        InfluenceTarget target,
        InfluenceEffect effect,
        double? expectedDelta = null)
    {
        InfluenceEntry entry = map.Single(item => item.FanGroupId == groupId && item.Target == target);
        Assert.Equal(MetricEvidence.Measured, entry.Evidence);
        Assert.Equal(effect, entry.Effect);
        if (expectedDelta is double value)
        {
            Assert.Equal(value, entry.DeltaCelsius ?? 0, 1);
        }
    }

    private static IReadOnlyList<FanTestSample> Group(
        DateTimeOffset start,
        string id,
        string name,
        double cpuReference,
        double gpuReference,
        double vrmReference,
        double caseReference,
        double cpuPerturb,
        double gpuPerturb,
        double vrmPerturb,
        double casePerturb) =>
    [
        Sample(start, id, name, FanTestStage.Reference, cpuReference, gpuReference, vrmReference, caseReference),
        .. PerturbCopies(start.AddSeconds(45), id, name, cpuPerturb, gpuPerturb, vrmPerturb, casePerturb),
    ];

    private static IReadOnlyList<FanTestSample> PerturbCopies(
        DateTimeOffset start,
        string id,
        string name,
        double cpu,
        double gpu,
        double vrm,
        double? caseTemp)
    {
        var samples = new List<FanTestSample>(ThermalDynamics.SettleWindowSamples);
        for (int index = 0; index < ThermalDynamics.SettleWindowSamples; index++)
        {
            samples.Add(Sample(
                start.AddSeconds(index),
                id,
                name,
                FanTestStage.Perturb,
                cpu,
                gpu,
                vrm,
                caseTemp,
                duty: 70,
                rpm: 1400));
        }

        return samples;
    }

    private static FanTestSample Sample(
        DateTimeOffset at,
        string id,
        string name,
        FanTestStage stage,
        double cpu,
        double gpu,
        double vrm,
        double? caseTemp,
        int duty = 40,
        double rpm = 800,
        bool settled = true)
    {
        var sensors = new List<SensorReading>
        {
            new("cpu-temp", "CPU", SensorKind.CpuTemperature, cpu, "°C"),
            new("gpu-temp", "GPU", SensorKind.GpuTemperature, gpu, "°C"),
            new("vrm-temp", "VRM", SensorKind.VrmTemperature, vrm, "°C"),
        };
        if (caseTemp is double value)
        {
            sensors.Add(new SensorReading("case-temp", "Case", SensorKind.CaseTemperature, value, "°C"));
        }

        var snapshot = new HardwareSnapshot(
            at,
            sensors,
            [
                new FanGroup(id, name, duty, rpm, "Demo controller", IsControllable: true),
            ],
            IsDemoHardware: true);
        return new FanTestSample(at, id, name, stage, snapshot, settled);
    }

    [Fact]
    public void Build_ignores_a_large_delta_when_duty_and_rpm_did_not_rise()
    {
        DateTimeOffset start = new(2026, 9, 19, 17, 0, 0, TimeSpan.Zero);
        string id = FakeHardwareBackend.TopFanId;
        FanTestSample[] samples =
        [
            Sample(start, id, "System Fan #1", FanTestStage.Reference, 70, 72, 60, 45, duty: 50, rpm: 982),
            .. SpeedCopies(start.AddSeconds(10), id, "System Fan #1", 64, 66.6, 59, 44, duty: 52, rpm: 915),
        ];

        IReadOnlyList<InfluenceEntry> map = InfluenceMapBuilder.Build(samples);
        InfluenceEntry gpu = map.Single(entry => entry.FanGroupId == id && entry.Target == InfluenceTarget.Gpu);
        Assert.Equal(MetricEvidence.Unknown, gpu.Evidence);
        Assert.Null(gpu.DeltaCelsius);
        Assert.Equal(FanTestReasons.NoSpeedUp, gpu.SkipReason);
    }

    [Fact]
    public void Build_keeps_a_real_speed_up_even_when_a_fake_bucket_is_larger()
    {
        DateTimeOffset start = new(2026, 9, 19, 17, 0, 0, TimeSpan.Zero);
        string id = FakeHardwareBackend.FrontFanId;
        FanTestSample[] samples =
        [
            Sample(start, id, "Front intake", FanTestStage.Reference, 70, 72, 60, 45, duty: 30, rpm: 1130),
            .. SpeedCopies(start.AddSeconds(10), id, "Front intake", 64, 60, 59, 44, duty: 32, rpm: 1100),
            .. SpeedCopies(start.AddSeconds(40), id, "Front intake", 68, 68, 59, 44, duty: 85, rpm: 3360),
        ];

        IReadOnlyList<InfluenceEntry> map = InfluenceMapBuilder.Build(samples);
        InfluenceEntry gpu = map.Single(entry => entry.FanGroupId == id && entry.Target == InfluenceTarget.Gpu);
        Assert.Equal(MetricEvidence.Measured, gpu.Evidence);
        Assert.Equal(4.0, gpu.DeltaCelsius ?? 0, 1);
        Assert.Equal(85, gpu.DutyAfter);
        Assert.Equal(3360, gpu.RpmAfter);
    }

    [Fact]
    public void Build_marks_unsettled_holds_unknown_even_when_temps_moved()
    {
        DateTimeOffset start = new(2026, 9, 21, 1, 0, 0, TimeSpan.Zero);
        string id = FakeHardwareBackend.FrontFanId;
        FanTestSample[] samples =
        [
            Sample(start, id, "Front intake", FanTestStage.Reference, 70, 72, 60, 45, duty: 30, rpm: 1130, settled: false),
            .. SpeedCopies(
                start.AddSeconds(10),
                id,
                "Front intake",
                64,
                60,
                59,
                44,
                duty: 85,
                rpm: 3360,
                settled: false),
        ];

        IReadOnlyList<InfluenceEntry> map = InfluenceMapBuilder.Build(samples);
        Assert.All(
            map.Where(entry => entry.FanGroupId == id),
            entry =>
            {
                Assert.Equal(MetricEvidence.Unknown, entry.Evidence);
                Assert.Null(entry.DeltaCelsius);
                Assert.Equal(FanTestReasons.Unsettled, entry.SkipReason);
            });
    }

    [Fact]
    public void Build_uses_only_settled_holds_for_measured_delta()
    {
        DateTimeOffset start = new(2026, 9, 21, 1, 0, 0, TimeSpan.Zero);
        string id = FakeHardwareBackend.FrontFanId;
        FanTestSample[] samples =
        [
            Sample(start, id, "Front intake", FanTestStage.Reference, 70, 72, 60, 45, duty: 30, rpm: 1130),
            .. SpeedCopies(start.AddSeconds(10), id, "Front intake", 60, 55, 59, 44, duty: 40, rpm: 1200, settled: false),
            .. SpeedCopies(start.AddSeconds(40), id, "Front intake", 68, 68, 59, 44, duty: 85, rpm: 3360),
        ];

        IReadOnlyList<InfluenceEntry> map = InfluenceMapBuilder.Build(samples);
        InfluenceEntry gpu = map.Single(entry => entry.FanGroupId == id && entry.Target == InfluenceTarget.Gpu);
        Assert.Equal(MetricEvidence.Measured, gpu.Evidence);
        Assert.Equal(4.0, gpu.DeltaCelsius ?? 0, 1);
        Assert.Equal(85, gpu.DutyAfter);
        Assert.Equal(3360, gpu.RpmAfter);
    }

    private static IReadOnlyList<FanTestSample> SpeedCopies(
        DateTimeOffset start,
        string id,
        string name,
        double cpu,
        double gpu,
        double vrm,
        double? caseTemp,
        int duty,
        double rpm,
        bool settled = true)
    {
        var samples = new List<FanTestSample>(ThermalDynamics.SettleWindowSamples);
        for (int index = 0; index < ThermalDynamics.SettleWindowSamples; index++)
        {
            samples.Add(Sample(
                start.AddSeconds(index),
                id,
                name,
                FanTestStage.Perturb,
                cpu,
                gpu,
                vrm,
                caseTemp,
                duty,
                rpm,
                settled));
        }

        return samples;
    }
}
