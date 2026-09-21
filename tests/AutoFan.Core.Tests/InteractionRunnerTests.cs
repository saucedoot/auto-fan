using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class InteractionRunnerTests
{
    [Fact]
    public async Task Run_tests_pair_under_low_load_and_skips_pump_gpu()
    {
        var inner = new FakeHardwareBackend();
        Assert.True(inner.TrySetDuty(FakeHardwareBackend.FrontFanId, 0).Accepted);
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryInteractionStore();
        var clock = new ManualTimeProvider();
        var runner = new InteractionRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock));

        InteractionRun run = await runner.RunAsync(influence: PairInfluence());

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Equal([WorkloadLevel.Low], workload.History);
        Assert.True(workload.StopCount >= 1);
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.PumpId}", StringComparison.Ordinal));
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.AmdGpuFanId}", StringComparison.Ordinal));
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.FrontFanId}", StringComparison.Ordinal));
        Assert.Contains(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.RearFanId}", StringComparison.Ordinal));
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.PumpId);
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.AmdGpuFanId && skip.Reason == FanTestReasons.Gpu);
        Assert.Contains(run.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.FrontFanId && skip.Reason == FanTestReasons.NoRpm);
        Assert.Contains(run.Effects, entry => entry.Target == InfluenceTarget.Cpu && entry.Evidence != MetricEvidence.Inferred);
        Assert.Contains(run.Samples, sample => sample.Settled);
        Assert.True(HasAloneThenCombined(hardware.Events));
        Assert.Same(run, store.GetLatest());
    }

    [Fact]
    public async Task Run_waits_until_cpu_is_at_or_below_60()
    {
        var inner = new FakeHardwareBackend();
        inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 65);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryInteractionStore();
        var clock = new ManualTimeProvider();
        int delays = 0;
        var runner = new InteractionRunner(
            inner,
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
                if (delays == 2)
                {
                    inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 50);
                }

                return Task.CompletedTask;
            });

        InteractionRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.True(delays >= 2);
        Assert.Equal(WorkloadLevel.Low, workload.History[0]);
    }

    [Fact]
    public async Task Run_thermal_abort_restores_and_persists()
    {
        var inner = new FakeHardwareBackend();
        inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 2);
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryInteractionStore();
        var clock = new ManualTimeProvider();
        var runner = new InteractionRunner(
            hardware,
            workload,
            new FixedCompetingSoftwareScanner(),
            store,
            clock,
            FastSchedule(),
            Delay(clock));

        InteractionRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Aborted, run.Status);
        Assert.Contains("CPU", run.AbortDetail, StringComparison.Ordinal);
        Assert.True(workload.StopCount >= 1);
        Assert.Contains(hardware.Events, item => item == "restore");
        Assert.Same(run, store.GetLatest());
    }

    [Fact]
    public async Task Run_cancel_restores_and_persists()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new RecordingHardwareBackend(inner);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryInteractionStore();
        var clock = new ManualTimeProvider();
        using var cts = new CancellationTokenSource();
        var runner = new InteractionRunner(
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

        InteractionRun run = await runner.RunAsync(cts.Token, influence: PairInfluence());

        Assert.Equal(FanTestRunStatus.Cancelled, run.Status);
        Assert.True(workload.StopCount >= 1);
        Assert.Contains(hardware.Events, item => item == "restore");
    }

    [Fact]
    public async Task Run_timeout_holds_are_not_measured()
    {
        var inner = new FakeHardwareBackend();
        var store = new InMemoryInteractionStore();
        var clock = new ManualTimeProvider();
        int delays = 0;
        var runner = new InteractionRunner(
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

        InteractionRun run = await runner.RunAsync(influence: PairInfluence());

        Assert.True(delays >= 8);
        Assert.NotEqual(FanTestRunStatus.Cancelled, run.Status);
        Assert.DoesNotContain(run.Samples, sample => sample.Settled);
        Assert.DoesNotContain(
            run.Effects,
            entry => entry.Evidence == MetricEvidence.Measured && entry.ResidualCelsius is not null);
    }

    private static bool HasAloneThenCombined(IReadOnlyList<string> events)
    {
        IReadOnlyList<string> writes = events
            .Where(item => item.StartsWith("write:", StringComparison.Ordinal))
            .ToArray();
        if (writes.Count < 3)
        {
            return false;
        }

        int firstRestore = -1;
        for (int index = 0; index < events.Count; index++)
        {
            if (events[index].StartsWith("write:", StringComparison.Ordinal) && firstRestore < 0)
            {
                continue;
            }

            if (events[index] == "restore" && firstRestore < 0)
            {
                firstRestore = index;
            }
        }

        return firstRestore > 0 && writes.Count >= 3;
    }

    private static IReadOnlyList<InfluenceEntry> PairInfluence() =>
    [
        new(
            FakeHardwareBackend.RearFanId,
            "Rear exhaust",
            InfluenceTarget.Cpu,
            2.0,
            InfluenceEffect.Medium,
            MetricEvidence.Measured,
            30,
            55,
            360,
            660),
        new(
            FakeHardwareBackend.TopFanId,
            "Top exhaust",
            InfluenceTarget.Cpu,
            0.7,
            InfluenceEffect.Low,
            MetricEvidence.Measured,
            25,
            50,
            275,
            550),
    ];

    private static FanTestSchedule FastSchedule() =>
        new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1));

    private static Func<TimeSpan, CancellationToken, Task> Delay(ManualTimeProvider clock) =>
        (span, token) =>
        {
            token.ThrowIfCancellationRequested();
            clock.Advance(span);
            return Task.CompletedTask;
        };
}
