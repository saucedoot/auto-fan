using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class FanTestRunnerTests
{
    private static readonly HeatProfile StoredLow = new(
        HeatProfile.EverydayCpuWorkers,
        2560,
        1440,
        1,
        48);

    [Fact]
    public async Task Run_writes_one_motherboard_group_at_a_time_and_skips_pump_and_gpu()
    {
        var inner = new FakeHardwareBackend();
        int frontOriginal = DutyOf(inner, FakeHardwareBackend.FrontFanId);
        int rearOriginal = DutyOf(inner, FakeHardwareBackend.RearFanId);
        int topOriginal = DutyOf(inner, FakeHardwareBackend.TopFanId);
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Null(run.AbortDetail);
        Assert.Equal([StoredLow], workload.AppliedLow);
        Assert.DoesNotContain(HeatProfile.DefaultLow, workload.AppliedLow);
        Assert.True(workload.StopCount >= 1);
        Assert.Equal(frontOriginal, DutyOf(inner, FakeHardwareBackend.FrontFanId));
        Assert.Equal(rearOriginal, DutyOf(inner, FakeHardwareBackend.RearFanId));
        Assert.Equal(topOriginal, DutyOf(inner, FakeHardwareBackend.TopFanId));
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.PumpId}", StringComparison.Ordinal));
        Assert.Contains(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.GpuFanId}", StringComparison.Ordinal));
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.AmdGpuFanId}", StringComparison.Ordinal));
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15],
            DutiesWritten(hardware.Events, FakeHardwareBackend.FrontFanId));
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:15");
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:100");
        Assert.Contains(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.RearFanId}", StringComparison.Ordinal));
        Assert.Contains(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.TopFanId}", StringComparison.Ordinal));
        AssertWritesRestoreBetweenGroups(hardware.Events);
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.PumpId && skip.Reason == FanTestReasons.Pump);
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.AmdGpuFanId && skip.Reason == FanTestReasons.Gpu);
        Assert.DoesNotContain(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.GpuFanId);
        Assert.Contains(run.Samples, sample => sample.Settled);
        Assert.Contains(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.Target == InfluenceTarget.Cpu
                && entry.Evidence == MetricEvidence.Measured);
        Assert.Contains(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId && entry.Target == InfluenceTarget.Cpu);
        Assert.Contains(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.TopFanId
                && entry.Target == InfluenceTarget.Gpu
                && entry.Effect == InfluenceEffect.None);
        Assert.DoesNotContain(run.Influence, entry => entry.FanGroupId == FakeHardwareBackend.PumpId);
        Assert.Same(run, store.GetLatest());
    }

    [Fact]
    public async Task Run_tests_one_nvidia_gpu_and_skips_coupled_siblings()
    {
        var inner = new FakeHardwareBackend();
        inner.AddFan(
            "gpu-fan-2",
            "GPU Fan #2",
            duty: 40,
            maxRpm: 1900,
            isGpu: true,
            controllerName: "NVIDIA GeForce RTX 4070");
        inner.AddFan(
            "gpu-fan-3",
            "GPU Fan #3",
            duty: 40,
            maxRpm: 1800,
            isGpu: true,
            controllerName: "NVIDIA GeForce RTX 4070");
        var hardware = new RecordingHardwareBackend(inner);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Contains(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.GpuFanId}", StringComparison.Ordinal));
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith("write:gpu-fan-2", StringComparison.Ordinal));
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith("write:gpu-fan-3", StringComparison.Ordinal));
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == "gpu-fan-2" && skip.Reason == FanTestReasons.Coupled);
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == "gpu-fan-3" && skip.Reason == FanTestReasons.Coupled);
        Assert.DoesNotContain(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.GpuFanId);
        Assert.DoesNotContain(run.Influence, entry => entry.FanGroupId == "gpu-fan-2");
        Assert.DoesNotContain(run.Influence, entry => entry.FanGroupId == "gpu-fan-3");
    }

    [Fact]
    public async Task Run_reorders_screening_but_still_tests_every_eligible_group()
    {
        var inner = new FakeHardwareBackend();
        var reordered = new ReorderedFanGroupsBackend(
            inner,
            [
                FakeHardwareBackend.RearFanId,
                FakeHardwareBackend.TopFanId,
                FakeHardwareBackend.FrontFanId,
                FakeHardwareBackend.PumpId,
                FakeHardwareBackend.GpuFanId,
                FakeHardwareBackend.AmdGpuFanId,
            ]);
        var hardware = new RecordingHardwareBackend(reordered);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        IReadOnlyList<string> writes = hardware.Events
            .Where(item => item.StartsWith("write:", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(writes, item => item.StartsWith($"write:{FakeHardwareBackend.FrontFanId}", StringComparison.Ordinal));
        Assert.Contains(writes, item => item.StartsWith($"write:{FakeHardwareBackend.RearFanId}", StringComparison.Ordinal));
        Assert.Contains(writes, item => item.StartsWith($"write:{FakeHardwareBackend.TopFanId}", StringComparison.Ordinal));
        Assert.Contains(writes, item => item.StartsWith($"write:{FakeHardwareBackend.GpuFanId}", StringComparison.Ordinal));
        Assert.DoesNotContain(writes, item => item.StartsWith($"write:{FakeHardwareBackend.PumpId}", StringComparison.Ordinal));
        Assert.DoesNotContain(writes, item => item.StartsWith($"write:{FakeHardwareBackend.AmdGpuFanId}", StringComparison.Ordinal));
        Assert.StartsWith($"write:{FakeHardwareBackend.FrontFanId}", writes[0], StringComparison.Ordinal);
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.PumpId);
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.AmdGpuFanId);
    }

    [Fact]
    public async Task Run_thermal_abort_restores_and_persists_partial_map()
    {
        var inner = new FakeHardwareBackend();
        Assert.True(inner.TrySetDuty(FakeHardwareBackend.FrontFanId, 40).Accepted);
        inner.RestoreDefaults();
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        int delays = 0;
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(span);
                delays++;
                if (delays == 8)
                {
                    inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 2);
                }

                return Task.CompletedTask;
            },
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Aborted, run.Status);
        Assert.Contains("CPU", run.AbortDetail, StringComparison.Ordinal);
        Assert.True(workload.StopCount >= 1);
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.Contains(hardware.Events, item => item == "restore");
        Assert.Same(run, store.GetLatest());
        Assert.NotEmpty(run.Samples);
    }

    [Fact]
    public async Task Run_gpu_device_lost_stops_heat_and_restores()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator { HasFault = true };
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Aborted, run.Status);
        Assert.Contains("GPU reset", run.AbortDetail, StringComparison.Ordinal);
        Assert.True(workload.StopCount >= 1);
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.Contains(hardware.Events, item => item == "restore");
    }

    [Fact]
    public async Task Run_cancel_restores_and_persists()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        using var cts = new CancellationTokenSource();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            (span, token) =>
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                clock.Advance(span);
                return Task.CompletedTask;
            },
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync(cts.Token);

        Assert.Equal(FanTestRunStatus.Cancelled, run.Status);
        Assert.True(workload.StopCount >= 1);
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.Contains(hardware.Events, item => item == "restore");
        Assert.Same(run, store.GetLatest());
    }

    [Fact]
    public async Task Run_competing_software_blocks_writes()
    {
        var inner = new FakeHardwareBackend();
        int frontOriginal = DutyOf(inner, FakeHardwareBackend.FrontFanId);
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner("FanControl"),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Aborted, run.Status);
        Assert.Contains("FanControl", run.AbortDetail, StringComparison.Ordinal);
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith("write:", StringComparison.Ordinal));
        Assert.Equal(frontOriginal, DutyOf(inner, FakeHardwareBackend.FrontFanId));
        Assert.True(workload.StopCount >= 1);
        Assert.Same(run, store.GetLatest());
    }

    [Fact]
    public async Task Run_waits_while_temperatures_are_still_moving()
    {
        var inner = new FakeHardwareBackend();
        inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 50);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        int delays = 0;
        var runner = new FanTestRunner(
            inner,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            new FanTestSchedule(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1)),
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(span);
                delays++;
                inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 50 + (delays * 0.4));
                return Task.CompletedTask;
            },
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.True(delays >= 8);
        Assert.NotEqual(FanTestRunStatus.Cancelled, run.Status);
        Assert.DoesNotContain(run.Samples, sample => sample.Settled);
        Assert.Contains(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.Evidence == MetricEvidence.Unknown
                && entry.SkipReason == FanTestReasons.Unsettled);
        Assert.DoesNotContain(
            run.Influence,
            entry => entry.Evidence == MetricEvidence.Measured && entry.DeltaCelsius is not null);
    }

    [Fact]
    public async Task Run_bios_at_70_still_writes_below_bios_duties_loud_first()
    {
        var inner = new FakeHardwareBackend();
        Assert.True(inner.TrySetDuty(FakeHardwareBackend.FrontFanId, 70).Accepted);
        var hardware = new RecordingHardwareBackend(inner);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15],
            DutiesWritten(hardware.Events, FakeHardwareBackend.FrontFanId));
        Assert.Contains(15, DutiesWritten(hardware.Events, FakeHardwareBackend.FrontFanId));
        Assert.DoesNotContain(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.SkipReason == FanTestReasons.AlreadyAtMaxDuty);
        AssertWritesRestoreBetweenGroups(hardware.Events);
        Assert.False(inner.HasActiveSoftwareControl);
    }

    [Fact]
    public async Task Run_bios_at_100_still_writes_below_bios_and_is_not_already_at_max()
    {
        var inner = new FakeHardwareBackend();
        Assert.True(inner.TrySetDuty(FakeHardwareBackend.FrontFanId, 100).Accepted);
        var hardware = new RecordingHardwareBackend(inner);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Equal(
            [90, 75, 60, 45, 30, 15],
            DutiesWritten(hardware.Events, FakeHardwareBackend.FrontFanId));
        Assert.DoesNotContain(
            hardware.Events,
            item => item == $"write:{FakeHardwareBackend.FrontFanId}:100");
        Assert.DoesNotContain(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.SkipReason == FanTestReasons.AlreadyAtMaxDuty);
        Assert.DoesNotContain(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.Evidence == MetricEvidence.Unknown
                && entry.SkipReason == FanTestReasons.AlreadyAtMaxDuty);
    }

    [Fact]
    public async Task Run_drops_stalled_zero_rpm_screen_point()
    {
        var inner = new FakeHardwareBackend();
        inner.SetStallAtOrBelow(FakeHardwareBackend.FrontFanId, 15);
        var hardware = new RecordingHardwareBackend(inner);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:15");
        Assert.DoesNotContain(
            run.Samples,
            sample => sample.FanGroupId == FakeHardwareBackend.FrontFanId
                && InfluenceMapBuilder.IsSpeedStage(sample.Stage)
                && sample.Snapshot.FanGroups.First(fan => fan.Id == FakeHardwareBackend.FrontFanId).Rpm is not > 0);
        IReadOnlyList<FanSpeedCurve> curves = FanSpeedCurveBuilder.Build(run.Samples);
        Assert.DoesNotContain(
            curves.SelectMany(static curve => curve.Points),
            static point => point.Rpm == 0);
        Assert.DoesNotContain(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.Evidence == MetricEvidence.Measured
                && entry.DutyAfter == 15);
    }

    [Fact]
    public async Task Run_refines_only_groups_that_moved_a_temperature()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new CpuFollowsFanDutyBackend(inner, FakeHardwareBackend.FrontFanId, coolAtDuty: 90);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync(
            onlyGroupIds: [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.RearFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Contains(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.Target == InfluenceTarget.Cpu
                && entry.Evidence == MetricEvidence.Measured
                && entry.Effect is not null and not InfluenceEffect.None);
        Assert.True(InfluenceMapBuilder.MovedATemperature(run.Influence, FakeHardwareBackend.FrontFanId));
        Assert.False(InfluenceMapBuilder.MovedATemperature(run.Influence, FakeHardwareBackend.RearFanId));
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15, 85, 70, 50, 40, 20],
            DutiesWritten(hardware.Events, FakeHardwareBackend.FrontFanId));
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15],
            DutiesWritten(hardware.Events, FakeHardwareBackend.RearFanId));
        Assert.DoesNotContain(
            hardware.Events,
            item => item.StartsWith($"write:{FakeHardwareBackend.RearFanId}:85", StringComparison.Ordinal)
                || item == $"write:{FakeHardwareBackend.RearFanId}:20");
    }

    [Fact]
    public async Task Run_only_requested_groups_are_written()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new RecordingHardwareBackend(inner);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync(
            onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        IReadOnlyList<string> writes = hardware.Events
            .Where(item => item.StartsWith("write:", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(writes, item => item.StartsWith($"write:{FakeHardwareBackend.FrontFanId}", StringComparison.Ordinal));
        Assert.DoesNotContain(writes, item => item.StartsWith($"write:{FakeHardwareBackend.RearFanId}", StringComparison.Ordinal));
        Assert.DoesNotContain(writes, item => item.StartsWith($"write:{FakeHardwareBackend.TopFanId}", StringComparison.Ordinal));
        Assert.Contains(run.Influence, entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId);
        Assert.DoesNotContain(run.Influence, entry => entry.FanGroupId == FakeHardwareBackend.RearFanId);
    }

    [Fact]
    public async Task Run_tests_idle_connected_headers_and_skips_measured_empty_ones()
    {
        var inner = new FakeHardwareBackend();
        inner.AddFan(FanPresenceRunnerTests.IdleFanId, "Fan #7", duty: 0, maxRpm: 1500);
        inner.AddFan(FanPresenceRunnerTests.EmptyHeaderId, "Fan #8", duty: 0, maxRpm: 1800, respondsToDuty: false);
        var hardware = new RecordingHardwareBackend(inner);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var presence = new FanPresence(
            true,
            [FakeHardwareBackend.FrontFanId, FanPresenceRunnerTests.IdleFanId],
            [FanPresenceRunnerTests.EmptyHeaderId]);
        var runner = new FanTestRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            presence: presence,
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Contains(
            hardware.Events,
            item => item.StartsWith($"write:{FanPresenceRunnerTests.IdleFanId}", StringComparison.Ordinal));
        Assert.DoesNotContain(
            hardware.Events,
            item => item.StartsWith($"write:{FanPresenceRunnerTests.EmptyHeaderId}", StringComparison.Ordinal));
        Assert.Contains(
            run.Skipped,
            skip => skip.FanGroupId == FanPresenceRunnerTests.EmptyHeaderId
                && skip.Reason == FanTestReasons.NoRpm);
    }

    [Fact]
    public async Task Run_skips_nvidia_gpu_fans_when_gpu_heat_is_not_useful()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            gpuHeatUseful: false,
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.DoesNotContain(
            hardware.Events,
            item => item.StartsWith($"write:{FakeHardwareBackend.GpuFanId}", StringComparison.Ordinal));
        Assert.Contains(
            run.Skipped,
            skip => skip.FanGroupId == FakeHardwareBackend.GpuFanId
                && skip.Reason == FanTestReasons.GpuHeatInsufficient);
        Assert.Contains(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.Target == InfluenceTarget.Cpu
                && entry.Evidence == MetricEvidence.Measured);
        Assert.All(
            run.Influence.Where(entry => entry.Target == InfluenceTarget.Gpu),
            entry =>
            {
                Assert.Equal(MetricEvidence.Unknown, entry.Evidence);
                Assert.Equal(FanTestReasons.GpuHeatInsufficient, entry.SkipReason);
                Assert.Null(entry.DeltaCelsius);
            });
    }

    private static void AssertWritesRestoreBetweenGroups(IReadOnlyList<string> events)
    {
        IReadOnlyList<string> writes = events
            .Where(item => item.StartsWith("write:", StringComparison.Ordinal))
            .ToArray();
        Assert.True(writes.Count >= 2);
        for (int index = 0; index < writes.Count - 1; index++)
        {
            int first = IndexOf(events, writes[index]);
            int second = IndexOf(events, writes[index + 1]);
            Assert.Contains("restore", events.Skip(first).Take(second - first));
        }
    }

    private static int IndexOf(IReadOnlyList<string> events, string value)
    {
        for (int index = 0; index < events.Count; index++)
        {
            if (events[index] == value)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"Event '{value}' was not recorded.");
    }

    [Fact]
    public async Task Run_marks_drifting_cpu_unknown_and_keeps_stable_gpu()
    {
        var inner = new FakeHardwareBackend();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            inner,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            stability: new ReferenceAssessment(
                new SensorStabilityResult(
                    SensorKind.CpuTemperature,
                    SensorStability.Drifting,
                    1.6,
                    3.2,
                    [60.8, 61.6, 62.4]),
                new SensorStabilityResult(
                    SensorKind.GpuTemperature,
                    SensorStability.Stable,
                    0.2,
                    0.5,
                    [54.0, 54.1, 54.0])),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Contains(
            run.Influence,
            entry => entry.Target == InfluenceTarget.Cpu
                && entry.Evidence == MetricEvidence.Unknown
                && entry.SkipReason == FanTestReasons.CpuNotStable);
        Assert.DoesNotContain(
            run.Influence,
            entry => entry.Target == InfluenceTarget.Gpu && entry.SkipReason == FanTestReasons.GpuNotStable);
    }

    [Fact]
    public async Task Run_applies_the_stored_low_profile_not_default_low()
    {
        Assert.NotEqual(HeatProfile.DefaultLow, StoredLow);
        var inner = new FakeHardwareBackend();
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            inner,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Equal([StoredLow], workload.AppliedLow);
        Assert.DoesNotContain(HeatProfile.DefaultLow, workload.AppliedLow);
        Assert.Equal(StoredLow, workload.LockedLow);
    }

    [Fact]
    public async Task Run_missing_stored_profile_does_not_heat_and_does_not_write()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: null);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Aborted, run.Status);
        Assert.Equal(HeatProfile.MissingLampDetail, run.AbortDetail);
        Assert.Empty(workload.AppliedLow);
        Assert.Empty(workload.History);
        Assert.Null(workload.LockedLow);
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith("write:", StringComparison.Ordinal));
        Assert.Empty(run.Samples);
        Assert.Same(run, store.GetLatest());
    }

    [Fact]
    public async Task Run_everyday_after_low_completed_uses_coarse_grid_on_movers_only()
    {
        HeatProfile storedEveryday = new(HeatProfile.EverydayCpuWorkers, 1280, 720, 1, 12);
        var inner = new FakeHardwareBackend();
        Assert.True(inner.TrySetDuty(FakeHardwareBackend.FrontFanId, 70).Accepted);
        var hardware = new SensorFollowsFanDutyBackend(
            inner,
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.CpuSensorId,
            coolAtDuty: 90);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow,
            everydayHeat: storedEveryday);

        FanTestRun run = await runner.RunAsync(
            onlyGroupIds: [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.RearFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Equal([StoredLow], workload.AppliedLow);
        Assert.Equal([storedEveryday], workload.AppliedEveryday);
        Assert.DoesNotContain(HeatProfile.Everyday, workload.AppliedEveryday);
        Assert.Contains(run.Samples, sample => sample.HeatId == HeatId.Low && sample.Settled);
        Assert.Contains(run.Samples, sample => sample.HeatId == HeatId.Everyday);
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15, 85, 70, 50, 40, 20, 100, 70, 40, 20, 85, 55],
            DutiesWritten(hardware.Events, FakeHardwareBackend.FrontFanId));
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15],
            DutiesWritten(hardware.Events, FakeHardwareBackend.RearFanId));
        Assert.DoesNotContain(
            hardware.Events,
            item => item.StartsWith($"write:{FakeHardwareBackend.RearFanId}:55", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_skips_everyday_when_low_aborts()
    {
        var inner = new FakeHardwareBackend();
        Assert.True(inner.TrySetDuty(FakeHardwareBackend.FrontFanId, 40).Accepted);
        inner.RestoreDefaults();
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        int delays = 0;
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(span);
                delays++;
                if (delays == 8)
                {
                    inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 2);
                }

                return Task.CompletedTask;
            },
            lowHeat: StoredLow,
            everydayHeat: HeatProfile.Everyday);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Aborted, run.Status);
        Assert.Empty(workload.AppliedEveryday);
        Assert.DoesNotContain(run.Samples, sample => sample.HeatId == HeatId.Everyday);
    }

    [Fact]
    public async Task Run_missing_everyday_lamp_does_not_heat_everyday_after_low()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new SensorFollowsFanDutyBackend(
            inner,
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.CpuSensorId,
            coolAtDuty: 90);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow,
            everydayHeat: null);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Equal([StoredLow], workload.AppliedLow);
        Assert.Empty(workload.AppliedEveryday);
        Assert.DoesNotContain(run.Samples, sample => sample.HeatId == HeatId.Everyday);
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15, 85, 70, 50, 40, 20],
            DutiesWritten(hardware.Events, FakeHardwareBackend.FrontFanId));
    }

    [Fact]
    public async Task Run_everyday_skips_nvidia_when_everyday_gpu_rise_is_not_enough()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new DualFollowsFanDutyBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            gpuHeatUseful: true,
            lowHeat: StoredLow,
            everydayHeat: HeatProfile.Everyday,
            everydayGpuHeatUseful: false);

        FanTestRun run = await runner.RunAsync(
            onlyGroupIds: [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.GpuFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.NotEmpty(DutiesWritten(hardware.Events, FakeHardwareBackend.GpuFanId));
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15, 85, 70, 50, 40, 20],
            DutiesWritten(hardware.Events, FakeHardwareBackend.GpuFanId));
        Assert.Contains(
            run.Skipped,
            skip => skip.FanGroupId == FakeHardwareBackend.GpuFanId
                && skip.Reason == FanTestReasons.GpuHeatInsufficient);
        Assert.DoesNotContain(
            run.Samples,
            sample => sample.HeatId == HeatId.Everyday
                && sample.FanGroupId == FakeHardwareBackend.GpuFanId
                && InfluenceMapBuilder.IsSpeedStage(sample.Stage));
        Assert.Contains(run.Samples, sample => sample.HeatId == HeatId.Everyday);
    }

    [Fact]
    public async Task Run_everyday_drops_stalled_zero_rpm_and_timeout_is_not_settled()
    {
        var inner = new FakeHardwareBackend();
        inner.SetStallAtOrBelow(FakeHardwareBackend.FrontFanId, 20);
        var hardware = new SensorFollowsFanDutyBackend(
            inner,
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.CpuSensorId,
            coolAtDuty: 90);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        int delays = 0;
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            new FanTestSchedule(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1)),
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(span);
                delays++;
                if (workload.AppliedEveryday.Count > 0)
                {
                    inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 50 + (delays * 0.4));
                }

                return Task.CompletedTask;
            },
            lowHeat: StoredLow,
            everydayHeat: HeatProfile.Everyday);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:20");
        Assert.DoesNotContain(
            run.Samples.Where(sample => sample.HeatId == HeatId.Everyday),
            sample => sample.FanGroupId == FakeHardwareBackend.FrontFanId
                && InfluenceMapBuilder.IsSpeedStage(sample.Stage)
                && sample.Snapshot.FanGroups.First(fan => fan.Id == FakeHardwareBackend.FrontFanId).Rpm is not > 0);
        Assert.Contains(run.Samples, sample => sample.HeatId == HeatId.Low && sample.Settled);
        Assert.DoesNotContain(
            run.Samples.Where(sample => sample.HeatId == HeatId.Everyday && InfluenceMapBuilder.IsSpeedStage(sample.Stage)),
            sample => sample.Settled);
    }

    [Fact]
    public async Task Run_everyday_uses_its_own_thirty_minute_cap()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new SensorFollowsFanDutyBackend(
            inner,
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.CpuSensorId,
            coolAtDuty: 90);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        int delays = 0;
        bool stretchedLow = false;
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            new FanTestSchedule(TimeSpan.FromMinutes(40), TimeSpan.FromSeconds(1)),
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                delays++;
                if (!stretchedLow && delays == 8)
                {
                    stretchedLow = true;
                    clock.Advance(TimeSpan.FromMinutes(29));
                }

                clock.Advance(span);
                return Task.CompletedTask;
            },
            lowHeat: StoredLow,
            everydayHeat: HeatProfile.Everyday);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Equal([HeatProfile.Everyday], workload.AppliedEveryday);
        Assert.Contains(run.Samples, sample => sample.HeatId == HeatId.Everyday);
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:20");
    }

    [Fact]
    public async Task Run_skips_hot_when_neither_sensor_is_five_degrees_above_low()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new SensorFollowsFanDutyBackend(
            inner,
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.CpuSensorId,
            coolAtDuty: 90);
        var workload = new FakeWorkloadActuator
        {
            OnApplyHot = _ => inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 49),
        };
        var store = new InMemoryFanTestStore();
        var baselineStore = new InMemoryBaselineStore();
        baselineStore.Save(HotBaseline(low: StoredLow));
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow,
            baseline: baselineStore.GetLatest(),
            baselineStore: baselineStore);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.NotEmpty(workload.AppliedHot);
        Assert.Null(workload.LockedHot);
        Assert.Null(baselineStore.GetLatest()?.HotProfile);
        Assert.DoesNotContain(
            run.Samples,
            sample => sample.HeatId == HeatId.Hot && InfluenceMapBuilder.IsSpeedStage(sample.Stage));
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15, 85, 70, 50, 40, 20],
            DutiesWritten(hardware.Events, FakeHardwareBackend.FrontFanId));
        Assert.DoesNotContain(WorkloadLevel.High, workload.History);
    }

    [Fact]
    public async Task Run_keeps_hot_when_cpu_is_five_degrees_above_low_and_uses_dense_grid()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new SensorFollowsFanDutyBackend(
            inner,
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.CpuSensorId,
            coolAtDuty: 90);
        var workload = new FakeWorkloadActuator
        {
            OnApplyHot = _ => inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 51),
        };
        var store = new InMemoryFanTestStore();
        var baselineStore = new InMemoryBaselineStore();
        baselineStore.Save(HotBaseline(low: StoredLow));
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow,
            baseline: baselineStore.GetLatest(),
            baselineStore: baselineStore);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.NotNull(workload.LockedHot);
        Assert.Equal(HeatProfile.EverydayCpuWorkers, workload.LockedHot.CpuWorkers);
        Assert.Equal(workload.LockedHot, baselineStore.GetLatest()?.HotProfile);
        Assert.Contains(run.Samples, sample => sample.HeatId == HeatId.Hot && sample.Settled);
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15, 85, 70, 50, 40, 20, 100, 90, 75, 60, 45, 30, 15, 85, 70, 50, 40, 20],
            DutiesWritten(hardware.Events, FakeHardwareBackend.FrontFanId));
        Assert.DoesNotContain(WorkloadLevel.High, workload.History);
        Assert.All(workload.AppliedHot, profile => Assert.Equal(HeatProfile.EverydayCpuWorkers, profile.CpuWorkers));
    }

    [Fact]
    public async Task Run_keeps_hot_when_gpu_is_five_degrees_above_low()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new SensorFollowsFanDutyBackend(
            inner,
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.CpuSensorId,
            coolAtDuty: 90);
        var workload = new FakeWorkloadActuator
        {
            OnApplyHot = _ => inner.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 48),
        };
        var store = new InMemoryFanTestStore();
        var baselineStore = new InMemoryBaselineStore();
        baselineStore.Save(HotBaseline(low: StoredLow));
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            lowHeat: StoredLow,
            baseline: baselineStore.GetLatest(),
            baselineStore: baselineStore);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.NotNull(workload.LockedHot);
        Assert.Equal(workload.LockedHot, baselineStore.GetLatest()?.HotProfile);
        Assert.Contains(run.Samples, sample => sample.HeatId == HeatId.Hot);
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:15");
    }

    [Fact]
    public async Task Run_skips_hot_when_low_aborts()
    {
        var inner = new FakeHardwareBackend();
        Assert.True(inner.TrySetDuty(FakeHardwareBackend.FrontFanId, 40).Accepted);
        inner.RestoreDefaults();
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        int delays = 0;
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(span);
                delays++;
                if (delays == 8)
                {
                    inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 2);
                }

                return Task.CompletedTask;
            },
            lowHeat: StoredLow,
            everydayHeat: HeatProfile.Everyday);

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Aborted, run.Status);
        Assert.Empty(workload.AppliedHot);
        Assert.Null(workload.LockedHot);
        Assert.DoesNotContain(run.Samples, sample => sample.HeatId == HeatId.Hot);
    }

    [Fact]
    public async Task Run_hot_skips_nvidia_until_hot_gpu_is_fifteen_above_idle()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new DualFollowsFanDutyBackend(inner);
        var workload = new FakeWorkloadActuator
        {
            OnApplyHot = _ => inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 51),
        };
        var store = new InMemoryFanTestStore();
        var baselineStore = new InMemoryBaselineStore();
        baselineStore.Save(HotBaseline(low: StoredLow));
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock),
            gpuHeatUseful: true,
            lowHeat: StoredLow,
            baseline: baselineStore.GetLatest(),
            baselineStore: baselineStore);

        FanTestRun run = await runner.RunAsync(
            onlyGroupIds: [FakeHardwareBackend.FrontFanId, FakeHardwareBackend.GpuFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.NotNull(workload.LockedHot);
        Assert.Equal(
            [100, 90, 75, 60, 45, 30, 15, 85, 70, 50, 40, 20],
            DutiesWritten(hardware.Events, FakeHardwareBackend.GpuFanId));
        Assert.Contains(
            run.Skipped,
            skip => skip.FanGroupId == FakeHardwareBackend.GpuFanId
                && skip.Reason == FanTestReasons.GpuHeatInsufficient);
        Assert.DoesNotContain(
            run.Samples,
            sample => sample.HeatId == HeatId.Hot
                && sample.FanGroupId == FakeHardwareBackend.GpuFanId
                && InfluenceMapBuilder.IsSpeedStage(sample.Stage));
        Assert.Contains(
            run.Samples,
            sample => sample.HeatId == HeatId.Hot
                && sample.FanGroupId == FakeHardwareBackend.FrontFanId
                && InfluenceMapBuilder.IsSpeedStage(sample.Stage));
    }

    [Fact]
    public async Task Run_hot_uses_its_own_thirty_minute_cap()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new SensorFollowsFanDutyBackend(
            inner,
            FakeHardwareBackend.FrontFanId,
            FakeHardwareBackend.CpuSensorId,
            coolAtDuty: 90);
        var workload = new FakeWorkloadActuator
        {
            OnApplyHot = _ => inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 51),
        };
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        int delays = 0;
        bool stretchedLow = false;
        var runner = new FanTestRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            new FanTestSchedule(TimeSpan.FromMinutes(40), TimeSpan.FromSeconds(1)),
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                delays++;
                if (!stretchedLow && delays == 8)
                {
                    stretchedLow = true;
                    clock.Advance(TimeSpan.FromMinutes(29));
                }

                clock.Advance(span);
                return Task.CompletedTask;
            },
            lowHeat: StoredLow);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.NotEmpty(workload.AppliedHot);
        Assert.NotNull(workload.LockedHot);
        Assert.Contains(run.Samples, sample => sample.HeatId == HeatId.Hot);
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:15");
    }

    private static BaselineRun HotBaseline(HeatProfile low) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: null,
            GpuLoadAvailable: true,
            [
                new BaselineSample(
                    DateTimeOffset.UnixEpoch,
                    BaselinePhase.Idle,
                    new FakeHardwareBackend().ReadSnapshot()),
            ],
            [
                new BaselineMetric(
                    BaselineMetricNames.GpuRiseCelsius,
                    15.1,
                    "°C",
                    MetricEvidence.Measured),
            ],
            EverydayProfile: HeatProfile.Everyday,
            LowProfile: low);

    private static FanTestSchedule FastSchedule() =>
        new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));

    private static Func<TimeSpan, CancellationToken, Task> Delay(ManualTimeProvider clock) =>
        (span, token) =>
        {
            token.ThrowIfCancellationRequested();
            clock.Advance(span);
            return Task.CompletedTask;
        };

    private static int DutyOf(IHardwareBackend hardware, string fanGroupId)
    {
        FanGroup group = hardware.FanGroups.Single(fan => fan.Id == fanGroupId);
        return group.DutyCyclePercent ?? throw new InvalidOperationException($"Fan group '{fanGroupId}' has no duty.");
    }

    private static int[] DutiesWritten(IReadOnlyList<string> events, string fanGroupId)
    {
        string prefix = $"write:{fanGroupId}:";
        return events
            .Where(item => item.StartsWith(prefix, StringComparison.Ordinal))
            .Select(item => int.Parse(item.AsSpan(prefix.Length)))
            .ToArray();
    }

    private sealed class CpuFollowsFanDutyBackend : IHardwareBackend
    {
        private readonly FakeHardwareBackend _inner;
        private readonly RecordingHardwareBackend _recording;
        private readonly string _fanId;
        private readonly int _coolAtDuty;

        public CpuFollowsFanDutyBackend(FakeHardwareBackend inner, string fanId, int coolAtDuty)
        {
            _inner = inner;
            _recording = new RecordingHardwareBackend(inner);
            _fanId = fanId;
            _coolAtDuty = coolAtDuty;
            SyncCpu();
        }

        public List<string> Events => _recording.Events;

        public string DisplayName => _recording.DisplayName;

        public bool IsDemo => _recording.IsDemo;

        public IReadOnlyList<FanGroup> FanGroups => _recording.FanGroups;

        public bool HasActiveSoftwareControl => _recording.HasActiveSoftwareControl;

        public HardwareSnapshot ReadSnapshot() => _recording.ReadSnapshot();

        public DutySetResult TrySetDuty(string fanGroupId, int percent)
        {
            DutySetResult result = _recording.TrySetDuty(fanGroupId, percent);
            SyncCpu();
            return result;
        }

        public void RestoreDefaults()
        {
            _recording.RestoreDefaults();
            SyncCpu();
        }

        private void SyncCpu()
        {
            int duty = _inner.FanGroups.Single(fan => fan.Id == _fanId).DutyCyclePercent ?? 0;
            _inner.OverrideTemperature(
                FakeHardwareBackend.CpuSensorId,
                duty >= _coolAtDuty ? 38 : 45);
        }
    }

    private sealed class SensorFollowsFanDutyBackend : IHardwareBackend
    {
        private readonly FakeHardwareBackend _inner;
        private readonly RecordingHardwareBackend _recording;
        private readonly string _fanId;
        private readonly string _sensorId;
        private readonly int _coolAtDuty;

        public SensorFollowsFanDutyBackend(
            FakeHardwareBackend inner,
            string fanId,
            string sensorId,
            int coolAtDuty)
        {
            _inner = inner;
            _recording = new RecordingHardwareBackend(inner);
            _fanId = fanId;
            _sensorId = sensorId;
            _coolAtDuty = coolAtDuty;
            Sync();
        }

        public List<string> Events => _recording.Events;

        public string DisplayName => _recording.DisplayName;

        public bool IsDemo => _recording.IsDemo;

        public IReadOnlyList<FanGroup> FanGroups => _recording.FanGroups;

        public bool HasActiveSoftwareControl => _recording.HasActiveSoftwareControl;

        public HardwareSnapshot ReadSnapshot() => _recording.ReadSnapshot();

        public DutySetResult TrySetDuty(string fanGroupId, int percent)
        {
            DutySetResult result = _recording.TrySetDuty(fanGroupId, percent);
            Sync();
            return result;
        }

        public void RestoreDefaults()
        {
            _recording.RestoreDefaults();
            Sync();
        }

        private void Sync()
        {
            int duty = _inner.FanGroups.Single(fan => fan.Id == _fanId).DutyCyclePercent ?? 0;
            _inner.OverrideTemperature(_sensorId, duty >= _coolAtDuty ? 38 : 45);
        }
    }

    private sealed class DualFollowsFanDutyBackend : IHardwareBackend
    {
        private readonly FakeHardwareBackend _inner;
        private readonly RecordingHardwareBackend _recording;

        public DualFollowsFanDutyBackend(FakeHardwareBackend inner)
        {
            _inner = inner;
            _recording = new RecordingHardwareBackend(inner);
            Sync();
        }

        public List<string> Events => _recording.Events;

        public string DisplayName => _recording.DisplayName;

        public bool IsDemo => _recording.IsDemo;

        public IReadOnlyList<FanGroup> FanGroups => _recording.FanGroups;

        public bool HasActiveSoftwareControl => _recording.HasActiveSoftwareControl;

        public HardwareSnapshot ReadSnapshot() => _recording.ReadSnapshot();

        public DutySetResult TrySetDuty(string fanGroupId, int percent)
        {
            DutySetResult result = _recording.TrySetDuty(fanGroupId, percent);
            Sync();
            return result;
        }

        public void RestoreDefaults()
        {
            _recording.RestoreDefaults();
            Sync();
        }

        private void Sync()
        {
            int front = _inner.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent ?? 0;
            int gpu = _inner.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.GpuFanId).DutyCyclePercent ?? 0;
            _inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, front >= 90 ? 38 : 45);
            _inner.OverrideTemperature(FakeHardwareBackend.GpuSensorId, gpu >= 90 ? 36 : 44);
        }
    }
}
