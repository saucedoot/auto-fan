using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class ScreenRunnerTests
{
    [Fact]
    public async Task A_repeated_reference_is_modeled_and_not_a_measured_curve_point()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new RecordingHardwareBackend(inner);
        var clock = new ManualTimeProvider();
        var runner = new ScreenRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            new InMemoryFanTestStore(),
            clock,
            Delay(clock),
            lowHeat: new HeatProfile(HeatProfile.EverydayCpuWorkers, 2560, 1440, 1, 64));

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Completed, run.Status);
        Assert.Contains(run.Influence, entry => entry.Evidence == MetricEvidence.Modeled);
        Assert.DoesNotContain(run.Influence, entry => entry.Evidence == MetricEvidence.Measured);
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:35");
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:40");
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:70");
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:100");
        Assert.DoesNotContain(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:15");
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.Equal(WalkFansNext.SkipPairs, OptimizeWalkOutcome.AfterFans(run));
    }

    [Fact]
    public async Task Actuation_supplies_the_low_middle_and_high_duties()
    {
        var hardware = new FakeHardwareBackend();
        var clock = new ManualTimeProvider();
        var actuation = new FanActuation(
            FakeHardwareBackend.FrontFanId,
            "Front",
            35,
            400,
            MinimumStableDutyPercent: 30,
            StartDutyPercent: null,
            MaximumRpm: 1400,
            TachometerRepeats: true,
            Points: []);
        var runner = new ScreenRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            new InMemoryFanTestStore(),
            clock,
            Delay(clock),
            lowHeat: HeatProfile.CpuOnly,
            actuation: [actuation]);

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Contains(run.Influence, entry => entry.DutyAfter == 30);
        Assert.Contains(run.Influence, entry => entry.DutyAfter == 65);
        Assert.Contains(run.Influence, entry => entry.DutyAfter == 100);
        Assert.DoesNotContain(run.Influence, entry => entry.DutyAfter == 40);
    }

    [Fact]
    public async Task A_missing_lamp_refuses_the_screen()
    {
        var runner = new ScreenRunner(
            new FakeHardwareBackend(),
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            new InMemoryFanTestStore());

        FanTestRun run = await runner.RunAsync();

        Assert.Equal(FanTestRunStatus.Aborted, run.Status);
        Assert.Equal(HeatProfile.MissingLampDetail, run.AbortDetail);
    }

    [Fact]
    public void A_reference_that_does_not_come_back_is_not_a_fan_effect()
    {
        Assert.True(ScreenPlan.TemperatureReturned(50, 0.2, 50.3));
        Assert.False(ScreenPlan.TemperatureReturned(50, 0.2, 52));
    }

    private static Func<TimeSpan, CancellationToken, Task> Delay(ManualTimeProvider clock) =>
        (span, token) =>
        {
            token.ThrowIfCancellationRequested();
            clock.Advance(span);
            return Task.CompletedTask;
        };
}
