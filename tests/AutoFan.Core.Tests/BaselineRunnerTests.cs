using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class BaselineRunnerTests
{
    [Fact]
    public async Task Run_records_all_phases_without_writing_fans()
    {
        var hardware = new FakeHardwareBackend();
        hardware.IncludeAmbient(23);
        int originalDuty = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        Assert.True(hardware.TrySetDuty(FakeHardwareBackend.FrontFanId, 80).Accepted);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryBaselineStore();
        var clock = new ManualTimeProvider();
        var runner = new BaselineRunner(
            hardware,
            workload,
            store,
            clock,
            FastSchedule(),
            Delay(clock));

        BaselineRun run = await runner.RunAsync();

        Assert.Equal(BaselineRunStatus.Completed, run.Status);
        Assert.Null(run.AbortDetail);
        Assert.Equal(23, run.AmbientCelsius);
        Assert.True(run.GpuLoadAvailable);
        Assert.Contains(run.Samples, sample => sample.Phase == BaselinePhase.Idle);
        Assert.Contains(run.Samples, sample => sample.Phase == BaselinePhase.Everyday);
        Assert.Contains(run.Samples, sample => sample.Phase == BaselinePhase.Low);
        Assert.DoesNotContain(run.Samples, sample => sample.Phase == BaselinePhase.High);
        Assert.Contains(run.Samples, sample => sample.Phase == BaselinePhase.Reference);
        Assert.Contains(run.Samples, sample => sample.Phase == BaselinePhase.Cooldown);
        ReferenceAssessment? stability = ReferenceStability.FromBaseline(run);
        Assert.NotNull(stability);
        Assert.Equal(SensorStability.Stable, stability.Cpu.State);
        Assert.Equal(SensorStability.Stable, stability.Gpu.State);
        Assert.Equal(
            [WorkloadLevel.Idle, WorkloadLevel.Everyday, WorkloadLevel.Low, WorkloadLevel.Idle],
            workload.History);
        Assert.NotNull(workload.LockedLow);
        Assert.Equal(HeatProfile.EverydayCpuWorkers, workload.LockedLow.CpuWorkers);
        Assert.Equal(HeatProfile.Everyday, run.EverydayProfile);
        Assert.Equal(workload.LockedLow, run.LowProfile);
        Assert.NotNull(run.LowProfile);
        Assert.DoesNotContain(WorkloadLevel.High, workload.History);
        Assert.True(workload.StopCount >= 1);
        Assert.Equal(80, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.NotEqual(originalDuty, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.Same(run, store.GetLatest());
        Assert.Contains(
            run.Metrics,
            metric => metric.Name == BaselineMetricNames.AmbientCelsius
                && metric.Evidence == MetricEvidence.Measured
                && metric.Value == 23);
    }

    [Fact]
    public async Task Run_aborts_on_thermal_limit_stops_load_and_does_not_change_duty()
    {
        var hardware = new FakeHardwareBackend();
        Assert.True(hardware.TrySetDuty(FakeHardwareBackend.FrontFanId, 80).Accepted);
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 3);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryBaselineStore();
        var clock = new ManualTimeProvider();
        var runner = new BaselineRunner(
            hardware,
            workload,
            store,
            clock,
            FastSchedule(),
            Delay(clock));

        BaselineRun run = await runner.RunAsync();

        Assert.Equal(BaselineRunStatus.Aborted, run.Status);
        Assert.Contains("CPU", run.AbortDetail, StringComparison.Ordinal);
        Assert.True(workload.StopCount >= 1);
        Assert.Null(run.EverydayProfile);
        Assert.Null(run.LowProfile);
        Assert.Equal(80, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.True(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public async Task Run_records_missing_ambient_as_unknown()
    {
        var hardware = new FakeHardwareBackend();
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryBaselineStore();
        var clock = new ManualTimeProvider();
        var runner = new BaselineRunner(
            hardware,
            workload,
            store,
            clock,
            FastSchedule(),
            Delay(clock));

        BaselineRun run = await runner.RunAsync();

        Assert.Null(run.AmbientCelsius);
        Assert.Contains(
            run.Metrics,
            metric => metric.Name == BaselineMetricNames.AmbientCelsius
                && metric.Evidence == MetricEvidence.Unknown
                && metric.Value is null);
    }

    [Fact]
    public async Task Run_cancel_stops_load_and_does_not_write_fans()
    {
        var hardware = new FakeHardwareBackend();
        Assert.True(hardware.TrySetDuty(FakeHardwareBackend.RearFanId, 70).Accepted);
        var workload = new FakeWorkloadActuator();
        var store = new InMemoryBaselineStore();
        var clock = new ManualTimeProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new BaselineRunner(
            hardware,
            workload,
            store,
            clock,
            FastSchedule(),
            Delay(clock));

        BaselineRun run = await runner.RunAsync(cts.Token);

        Assert.Equal(BaselineRunStatus.Cancelled, run.Status);
        Assert.True(workload.StopCount >= 1);
        Assert.Equal(70, DutyOf(hardware, FakeHardwareBackend.RearFanId));
    }

    private static BaselineSchedule FastSchedule() =>
        new(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));

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
