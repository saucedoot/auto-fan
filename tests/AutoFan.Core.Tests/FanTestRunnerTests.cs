using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class FanTestRunnerTests
{
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
            Delay(clock));

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Null(run.AbortDetail);
        Assert.Equal([WorkloadLevel.Low], workload.History);
        Assert.True(workload.StopCount >= 1);
        Assert.Equal(frontOriginal, DutyOf(inner, FakeHardwareBackend.FrontFanId));
        Assert.Equal(rearOriginal, DutyOf(inner, FakeHardwareBackend.RearFanId));
        Assert.Equal(topOriginal, DutyOf(inner, FakeHardwareBackend.TopFanId));
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.PumpId}", StringComparison.Ordinal));
        Assert.Contains(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.GpuFanId}", StringComparison.Ordinal));
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.AmdGpuFanId}", StringComparison.Ordinal));
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:40");
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:70");
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:100");
        Assert.Contains(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.RearFanId}", StringComparison.Ordinal));
        Assert.Contains(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.TopFanId}", StringComparison.Ordinal));
        AssertWritesRestoreBetweenGroups(hardware.Events);
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.PumpId && skip.Reason == FanTestReasons.Pump);
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.AmdGpuFanId && skip.Reason == FanTestReasons.Gpu);
        Assert.DoesNotContain(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.GpuFanId);
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
            Delay(clock));

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
            Delay(clock));

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
            });

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
            Delay(clock));

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
            });

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
            Delay(clock));

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
            });

        FanTestRun run = await runner.RunAsync();

        Assert.True(delays >= 8);
        Assert.NotEqual(FanTestRunStatus.Cancelled, run.Status);
    }

    [Fact]
    public async Task Run_already_max_duty_is_unknown_not_none()
    {
        var inner = new FakeHardwareBackend();
        Assert.True(inner.TrySetDuty(FakeHardwareBackend.FrontFanId, 100).Accepted);
        var store = new InMemoryFanTestStore();
        var clock = new ManualTimeProvider();
        var runner = new FanTestRunner(
            inner,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock));

        FanTestRun run = await runner.RunAsync();

        Assert.Contains(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.Evidence == MetricEvidence.Unknown
                && entry.SkipReason == FanTestReasons.AlreadyAtMaxDuty);
        Assert.DoesNotContain(
            run.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.Effect == InfluenceEffect.None);
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
            Delay(clock));

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
            presence: presence);

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
            gpuHeatUseful: false);

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
                    [54.0, 54.1, 54.0])));

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
}
