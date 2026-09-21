using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class FanPresenceRunnerTests
{
    public const string IdleFanId = "sys-idle";
    public const string EmptyHeaderId = "sys-empty";

    [Fact]
    public async Task Run_sets_each_motherboard_header_to_100_and_keeps_headers_that_raise_rpm()
    {
        var inner = new FakeHardwareBackend();
        inner.AddFan(IdleFanId, "Fan #7", duty: 0, maxRpm: 1500);
        inner.AddFan(EmptyHeaderId, "Fan #8", duty: 0, maxRpm: 1800, respondsToDuty: false);
        int frontOriginal = DutyOf(inner, FakeHardwareBackend.FrontFanId);
        int idleOriginal = DutyOf(inner, IdleFanId);
        var hardware = new RecordingHardwareBackend(inner);
        var clock = new ManualTimeProvider();
        var runner = new FanPresenceRunner(
            hardware,
            new FixedCompetingSoftwareScanner(),
            clock,
            Delay(clock),
            timeout: TimeSpan.FromSeconds(2),
            samplePeriod: TimeSpan.FromMilliseconds(200));

        FanPresenceReport report = await runner.RunAsync();

        Assert.Equal(FanPresenceStatus.Completed, report.Status);
        Assert.Contains(FakeHardwareBackend.FrontFanId, report.ConnectedIds);
        Assert.Contains(FakeHardwareBackend.RearFanId, report.ConnectedIds);
        Assert.Contains(FakeHardwareBackend.TopFanId, report.ConnectedIds);
        Assert.Contains(IdleFanId, report.ConnectedIds);
        Assert.Contains(EmptyHeaderId, report.EmptyIds);
        Assert.Contains(FakeHardwareBackend.GpuFanId, report.ConnectedIds);
        Assert.DoesNotContain(FakeHardwareBackend.AmdGpuFanId, report.ConnectedIds);
        Assert.DoesNotContain(FakeHardwareBackend.AmdGpuFanId, report.EmptyIds);
        Assert.Contains(report.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.PumpId);
        Assert.Contains(report.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.AmdGpuFanId);
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.FrontFanId}:100");
        Assert.Contains(hardware.Events, item => item == $"write:{IdleFanId}:100");
        Assert.Contains(hardware.Events, item => item == $"write:{EmptyHeaderId}:100");
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.GpuFanId}:100");
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.AmdGpuFanId}", StringComparison.Ordinal));
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith($"write:{FakeHardwareBackend.PumpId}", StringComparison.Ordinal));
        Assert.Contains("restore", hardware.Events);
        Assert.Equal(frontOriginal, DutyOf(inner, FakeHardwareBackend.FrontFanId));
        Assert.Equal(idleOriginal, DutyOf(inner, IdleFanId));
        Assert.False(inner.HasActiveSoftwareControl);
    }

    [Fact]
    public void HasFan_treats_any_spinning_header_as_connected()
    {
        Assert.True(FanPresenceRunner.HasFan(800));
        Assert.True(FanPresenceRunner.RpmResponded(1200, 1210));
        Assert.False(FanPresenceRunner.HasFan(0));
        Assert.False(FanPresenceRunner.RpmResponded(0, 0));
        Assert.False(FanPresenceRunner.RpmResponded(900, 0));
    }

    [Fact]
    public async Task Run_writes_nvidia_gpu_fans_once_and_marks_each_by_rpm()
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
        var clock = new ManualTimeProvider();
        var runner = new FanPresenceRunner(
            hardware,
            new FixedCompetingSoftwareScanner(),
            clock,
            Delay(clock),
            timeout: TimeSpan.FromSeconds(2),
            samplePeriod: TimeSpan.FromMilliseconds(200));

        FanPresenceReport report = await runner.RunAsync();

        Assert.Equal(FanPresenceStatus.Completed, report.Status);
        Assert.Contains(FakeHardwareBackend.GpuFanId, report.ConnectedIds);
        Assert.Contains("gpu-fan-2", report.ConnectedIds);
        Assert.Contains("gpu-fan-3", report.ConnectedIds);
        Assert.Single(hardware.Events, item => item.StartsWith("write:gpu-fan", StringComparison.Ordinal));
        Assert.Contains(hardware.Events, item => item == $"write:{FakeHardwareBackend.GpuFanId}:100");
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith("write:gpu-fan-2", StringComparison.Ordinal));
        Assert.DoesNotContain(hardware.Events, item => item.StartsWith("write:gpu-fan-3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_keeps_a_hub_that_is_already_spinning()
    {
        var inner = new FakeHardwareBackend();
        inner.AddFan("sys-hub", "System Fan #1", duty: 100, maxRpm: 1500);
        var hardware = new RecordingHardwareBackend(inner);
        var clock = new ManualTimeProvider();
        var runner = new FanPresenceRunner(
            hardware,
            new FixedCompetingSoftwareScanner(),
            clock,
            Delay(clock),
            timeout: TimeSpan.FromSeconds(2),
            samplePeriod: TimeSpan.FromMilliseconds(200));

        FanPresenceReport report = await runner.RunAsync();

        Assert.Contains("sys-hub", report.ConnectedIds);
        Assert.DoesNotContain("sys-hub", report.EmptyIds);
    }

    [Fact]
    public async Task Run_aborts_and_restores_when_already_over_the_ceiling()
    {
        var inner = new FakeHardwareBackend();
        inner.AddFan(IdleFanId, "Fan #7", duty: 0, maxRpm: 1500);
        inner.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 96);
        var hardware = new RecordingHardwareBackend(inner);
        var runner = new FanPresenceRunner(
            hardware,
            new FixedCompetingSoftwareScanner(),
            new ManualTimeProvider(),
            Delay(new ManualTimeProvider()),
            timeout: TimeSpan.FromSeconds(1),
            samplePeriod: TimeSpan.FromMilliseconds(200));

        FanPresenceReport report = await runner.RunAsync();

        Assert.Equal(FanPresenceStatus.Aborted, report.Status);
        Assert.False(inner.HasActiveSoftwareControl);
        Assert.Contains(hardware.Events, item => item == "restore");
    }

    [Fact]
    public async Task Run_does_not_write_when_competing_software_is_running()
    {
        var inner = new FakeHardwareBackend();
        inner.AddFan(IdleFanId, "Fan #7", duty: 0, maxRpm: 1500);
        var hardware = new RecordingHardwareBackend(inner);
        var runner = new FanPresenceRunner(
            hardware,
            new FixedCompetingSoftwareScanner("FanControl"),
            new ManualTimeProvider(),
            Delay(new ManualTimeProvider()),
            timeout: TimeSpan.FromSeconds(1),
            samplePeriod: TimeSpan.FromMilliseconds(200));

        FanPresenceReport report = await runner.RunAsync();

        Assert.DoesNotContain(hardware.Events, item => item.StartsWith("write:", StringComparison.Ordinal));
        Assert.Contains(IdleFanId, report.EmptyIds.Concat(report.Skipped.Select(skip => skip.FanGroupId)));
        Assert.False(inner.HasActiveSoftwareControl);
    }

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
