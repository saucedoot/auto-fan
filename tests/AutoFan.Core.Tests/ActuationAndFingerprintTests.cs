using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class ActuationAndFingerprintTests
{
    [Fact]
    public async Task A_downward_sweep_records_rpm_and_restores()
    {
        var hardware = new FakeHardwareBackend();
        var clock = new ManualTimeProvider();
        var calibrator = new ActuationCalibrator(
            hardware,
            clock,
            Delay(clock),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(200));
        FanActuation measured;
        using (var session = new SafeFanSession(hardware, new FixedCompetingSoftwareScanner(), clock))
        {
            FanGroup front = hardware.FanGroups.Single(group => group.Id == FakeHardwareBackend.FrontFanId);
            measured = await calibrator.MeasureAsync(session, front);
        }

        Assert.Equal(15, measured.MinimumStableDutyPercent);
        Assert.Null(measured.StartDutyPercent);
        Assert.Equal(1400, measured.MaximumRpm);
        Assert.True(measured.TachometerRepeats);
        Assert.Contains(measured.Points, point => point.DutyPercent == 100 && point.Rpm == 1400);
        Assert.DoesNotContain(measured.Points, point => point.DutyPercent == 0);
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public async Task Start_duty_is_kept_only_when_it_is_higher_than_the_running_minimum()
    {
        var hardware = new FakeHardwareBackend();
        hardware.SetSpinThresholds(FakeHardwareBackend.FrontFanId, keepAliveDuty: 20, startDuty: 40);
        var clock = new ManualTimeProvider();
        var calibrator = new ActuationCalibrator(
            hardware,
            clock,
            Delay(clock),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(200));
        FanActuation measured;
        using (var session = new SafeFanSession(hardware, new FixedCompetingSoftwareScanner(), clock))
        {
            FanGroup front = hardware.FanGroups.Single(group => group.Id == FakeHardwareBackend.FrontFanId);
            measured = await calibrator.MeasureAsync(session, front);
        }

        Assert.Equal(30, measured.MinimumStableDutyPercent);
        Assert.Equal(40, measured.StartDutyPercent);
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void Rpm_repeat_rejects_a_large_gap()
    {
        Assert.True(ActuationCalibrator.RpmRepeats(1000, 1040));
        Assert.False(ActuationCalibrator.RpmRepeats(1000, 1200));
        Assert.False(ActuationCalibrator.RpmRepeats(0, 1000));
    }

    [Fact]
    public void Fingerprint_matches_the_gpu_id_and_not_the_display_name()
    {
        var left = new TopologyFingerprint(
            "Board",
            "CPU",
            "42",
            "NVIDIA GeForce",
            ["cpu-temp"],
            [new FanTopology("front", "Demo", null, 30, 40, 1400)]);
        TopologyFingerprint renamed = left with { GpuName = "A different label" };
        TopologyFingerprint otherCard = left with { GpuHardwareId = "99" };
        TopologyFingerprint missingId = left with { GpuHardwareId = null };
        TopologyFingerprint otherFan = left with
        {
            Fans = [new FanTopology("rear", "Demo", null, 30, null, 1200)],
        };

        Assert.True(left.Matches(renamed));
        Assert.False(left.Matches(otherCard));
        Assert.False(left.Matches(missingId));
        Assert.False(left.Matches(otherFan));
    }

    [Fact]
    public void Nvidia_writes_do_not_fall_back_to_another_card()
    {
        NvidiaGpuChoice[] cards =
        [
            new(10, "NVIDIA GeForce RTX 4070"),
            new(20, "NVIDIA GeForce RTX 4070"),
        ];

        Assert.Equal(10u, NvidiaGpuSelector.Select(cards, "10", "NVIDIA GeForce RTX 4070"));
        Assert.Null(NvidiaGpuSelector.Select(cards, "99", "NVIDIA GeForce RTX 4070"));
        Assert.Null(NvidiaGpuSelector.Select(cards, hardwareId: null, "NVIDIA GeForce RTX 4070"));
        Assert.Equal(10u, NvidiaGpuSelector.Select([cards[0]], hardwareId: null, displayName: null));
        Assert.Null(NvidiaGpuSelector.Select(cards, hardwareId: null, "RTX 4090"));
    }

    [Fact]
    public async Task A_silent_pump_during_heat_aborts_and_restores()
    {
        var inner = new FakeHardwareBackend();
        var hardware = new RecordingHardwareBackend(inner);
        var clock = new ManualTimeProvider();
        int delays = 0;
        var runner = new FanTestRunner(
            hardware,
            new FakeWorkloadActuator(),
            new FixedCompetingSoftwareScanner(),
            new InMemoryFanTestStore(),
            clock,
            new FanTestSchedule(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)),
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(span);
                delays++;
                if (delays == 3)
                {
                    inner.SetStallAtOrBelow(FakeHardwareBackend.PumpId, 100);
                }

                return Task.CompletedTask;
            },
            lowHeat: new HeatProfile(HeatProfile.EverydayCpuWorkers, 2560, 1440, 1, 48));

        FanTestRun run = await runner.RunAsync(onlyGroupIds: [FakeHardwareBackend.FrontFanId]);

        Assert.Equal(FanTestRunStatus.Aborted, run.Status);
        Assert.Equal(PumpWatch.SilentDetail, run.AbortDetail);
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.Contains(hardware.Events, item => item == "restore");
    }

    private static Func<TimeSpan, CancellationToken, Task> Delay(ManualTimeProvider clock) =>
        (span, token) =>
        {
            token.ThrowIfCancellationRequested();
            clock.Advance(span);
            return Task.CompletedTask;
        };
}
