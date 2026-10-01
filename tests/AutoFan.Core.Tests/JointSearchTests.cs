using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class JointSearchTests
{
    [Fact]
    public void One_fan_faster_and_another_slower_is_not_quieter()
    {
        var max = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["front"] = 1000,
            ["rear"] = 1000,
        };
        HardwareSnapshot before = Snapshot(
            ("front", 800),
            ("rear", 800));
        HardwareSnapshot after = Snapshot(
            ("front", 600),
            ("rear", 950));

        Assert.False(FanEffort.LikelyQuieter(before, after, max));
        Assert.True(FanEffort.Of(after, max).Total < FanEffort.Of(before, max).Total);
    }

    [Fact]
    public void All_slower_fans_can_be_called_quieter()
    {
        var max = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["front"] = 1000,
            ["pump"] = 2000,
        };
        HardwareSnapshot before = Snapshot(
            ("front", (double?)800, FanGroupKind.Fan),
            ("pump", (double?)1000, FanGroupKind.Pump));
        HardwareSnapshot after = Snapshot(
            ("front", (double?)500, FanGroupKind.Fan),
            ("pump", (double?)1000, FanGroupKind.Pump));

        Assert.True(FanEffort.LikelyQuieter(before, after, max));
        Assert.Equal(0.5, FanEffort.Ratio(500, 1000));
        Assert.Null(FanEffort.Ratio(null, 1000));
    }

    [Fact]
    public void A_missing_speed_cannot_be_called_quieter()
    {
        var max = new Dictionary<string, double>(StringComparer.Ordinal) { ["front"] = 1000 };
        HardwareSnapshot before = Snapshot(("front", 800));
        HardwareSnapshot after = Snapshot(("front", null));

        Assert.False(FanEffort.LikelyQuieter(before, after, max));
    }

    [Fact]
    public void A_predicted_overheat_is_not_tried()
    {
        HardwareSnapshot reference = HeatSnapshot(89);
        InfluenceEntry[] influence =
        [
            new("front", "Front", InfluenceTarget.Cpu, 3, null, MetricEvidence.Modeled, 40, 70, 500, 800),
            new("front", "Front", InfluenceTarget.Gpu, 0, null, MetricEvidence.Modeled, 40, 70, 500, 800),
        ];

        IReadOnlyList<JointVector> vectors = JointPlan.Select(
            reference,
            influence,
            [],
            ThermalAbortLimits.Floor);

        Assert.DoesNotContain(vectors, vector => vector.Commands.Any(command => command.DutyPercent == 70));
    }

    [Fact]
    public void A_pair_is_added_only_once_for_a_large_combination()
    {
        DutyCommand[] vector = [new("a", 40), new("b", 40), new("c", 40)];
        InfluenceEntry[] influence =
        [
            new("a", "A", InfluenceTarget.Cpu, -2, null, MetricEvidence.Modeled, 70, 40, 900, 500),
            new("b", "B", InfluenceTarget.Cpu, -1, null, MetricEvidence.Modeled, 70, 40, 900, 500),
            new("c", "C", InfluenceTarget.Cpu, -0.2, null, MetricEvidence.Modeled, 70, 40, 900, 500),
        ];

        IReadOnlyList<DutyCommand>? pair = JointPlan.OnePair(vector, influence, residualTooLarge: true, pairAlreadyUsed: false);
        Assert.NotNull(pair);
        Assert.Equal(2, pair.Count);
        Assert.Null(JointPlan.OnePair(vector, influence, residualTooLarge: true, pairAlreadyUsed: true));
        Assert.Null(JointPlan.OnePair([vector[0], vector[1]], influence, residualTooLarge: true, pairAlreadyUsed: false));
    }

    [Fact]
    public async Task The_search_stops_as_a_draft_and_restores()
    {
        var inner = new FakeHardwareBackend();
        var clock = new ManualTimeProvider();
        var screen = new FanTestRun(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            FanTestRunStatus.Completed,
            null,
            true,
            [],
            [
                new(
                    FakeHardwareBackend.FrontFanId,
                    "Front",
                    InfluenceTarget.Cpu,
                    -1,
                    null,
                    MetricEvidence.Modeled,
                    35,
                    30,
                    490,
                    420),
                new(
                    FakeHardwareBackend.FrontFanId,
                    "Front",
                    InfluenceTarget.Gpu,
                    -0.5,
                    null,
                    MetricEvidence.Modeled,
                    35,
                    30,
                    490,
                    420),
            ],
            []);
        var runner = new JointRunner(
            inner,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            clock,
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(span);
                return Task.CompletedTask;
            },
            heat: new HeatProfile(HeatProfile.EverydayCpuWorkers, 2560, 1440, 1, 64),
            actuation:
            [
                new FanActuation(
                    FakeHardwareBackend.FrontFanId,
                    "Front",
                    35,
                    490,
                    30,
                    null,
                    1400,
                    true,
                    []),
            ]);

        JointSearchResult result = await runner.RunAsync(screen);

        Assert.Null(result.AbortDetail);
        Assert.True(result.Draft);
        Assert.False(result.Validated);
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.NotNull(result.Chosen);
        Assert.False(result.Chosen.LikelyQuieter);
    }

    private static HardwareSnapshot HeatSnapshot(double cpu) =>
        Snapshot([("front", (double?)800, FanGroupKind.Fan)], cpu);

    private static HardwareSnapshot Snapshot(params (string Id, double? Rpm, FanGroupKind Kind)[] fans) =>
        Snapshot(fans, cpu: 50);

    private static HardwareSnapshot Snapshot(
        (string Id, double? Rpm, FanGroupKind Kind)[] fans,
        double cpu)
    {
        return new HardwareSnapshot(
            DateTimeOffset.UnixEpoch,
            [
                new SensorReading("cpu", "CPU Package", SensorKind.CpuTemperature, cpu, "°C"),
                new SensorReading("gpu", "GPU", SensorKind.GpuTemperature, 45, "°C"),
            ],
            fans.Select(fan => new FanGroup(fan.Id, fan.Id, 40, fan.Rpm, "Demo", true, fan.Kind)).ToArray(),
            IsDemoHardware: true);
    }

    private static HardwareSnapshot Snapshot(params (string Id, double? Rpm)[] fans) =>
        Snapshot(fans.Select(fan => (fan.Id, fan.Rpm, FanGroupKind.Fan)).ToArray(), cpu: 50);
}
