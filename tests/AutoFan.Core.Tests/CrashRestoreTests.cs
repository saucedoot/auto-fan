using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class CrashRestoreTests
{
    [Fact]
    public void RestoreIfDirty_is_a_no_op_when_the_flag_is_clear()
    {
        using TempFlag file = TempFlag.Create();
        var hardware = new FakeHardwareBackend();
        Assert.True(hardware.TrySetDuty(FakeHardwareBackend.FrontFanId, 80).Accepted);

        bool restored = CrashRestore.RestoreIfDirty(file.Flag, hardware.RestoreDefaults);

        Assert.False(restored);
        Assert.False(file.Flag.IsActive);
        Assert.Equal(80, hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent);
        Assert.True(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void RestoreIfDirty_restores_and_clears_the_flag()
    {
        using TempFlag file = TempFlag.Create();
        file.Flag.MarkActive();
        var hardware = new FakeHardwareBackend();
        int original = hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent
            ?? throw new InvalidOperationException("Front fan has no duty.");
        Assert.True(hardware.TrySetDuty(FakeHardwareBackend.FrontFanId, 90).Accepted);

        bool restored = CrashRestore.RestoreIfDirty(file.Flag, hardware.RestoreDefaults);

        Assert.True(restored);
        Assert.False(file.Flag.IsActive);
        Assert.Equal(original, hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent);
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void Simulated_crash_restores_when_the_parent_dies_while_dirty()
    {
        using TempFlag file = TempFlag.Create();
        var hardware = new FakeHardwareBackend();
        var watchdog = new RecordingWatchdog();
        var lease = new SoftwareControlLease(file.Flag, watchdog);
        int original = hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.RearFanId).DutyCyclePercent
            ?? throw new InvalidOperationException("Rear fan has no duty.");

        lease.OnTakingControl();
        Assert.True(hardware.TrySetDuty(FakeHardwareBackend.RearFanId, 88).Accepted);
        Assert.True(file.Flag.IsActive);
        Assert.True(watchdog.IsArmed);

        watchdog.SimulateParentDeath(file.Flag, hardware);

        Assert.False(file.Flag.IsActive);
        Assert.False(watchdog.IsArmed);
        Assert.Equal(1, watchdog.DisarmCount);
        Assert.Equal(original, hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.RearFanId).DutyCyclePercent);
        Assert.False(hardware.HasActiveSoftwareControl);
    }
}

public sealed class SoftwareControlFlagTests
{
    [Fact]
    public void MarkActive_creates_the_file_and_Clear_removes_it()
    {
        using TempFlag file = TempFlag.Create();

        Assert.False(file.Flag.IsActive);
        file.Flag.MarkActive();
        Assert.True(file.Flag.IsActive);
        Assert.True(File.Exists(file.Flag.Path));
        file.Flag.Clear();
        Assert.False(file.Flag.IsActive);
        Assert.False(File.Exists(file.Flag.Path));
    }
}

public sealed class WatchdogLoopTests
{
    [Fact]
    public void TryParseParentPid_reads_watchdog_arguments()
    {
        Assert.True(WatchdogLoop.TryParseParentPid(["--watchdog", "--parent", "4242"], out int pid));
        Assert.Equal(4242, pid);
    }

    [Fact]
    public void TryParseParentPid_rejects_incomplete_arguments()
    {
        string[][] cases =
        [
            [],
            ["--parent", "12"],
            ["--watchdog"],
            ["--watchdog", "--parent", "0"],
            ["--watchdog", "--parent", "nope"],
        ];

        foreach (string[] args in cases)
        {
            Assert.False(WatchdogLoop.TryParseParentPid(args, out int pid));
            Assert.Equal(0, pid);
        }
    }
}

internal sealed class TempFlag : IDisposable
{
    private readonly string _directory;

    private TempFlag(string directory, SoftwareControlFlag flag)
    {
        _directory = directory;
        Flag = flag;
    }

    public SoftwareControlFlag Flag { get; }

    public static TempFlag Create()
    {
        string directory = Path.Combine(Path.GetTempPath(), "auto-fan-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, SoftwareControlFlag.FileName);
        return new TempFlag(directory, new SoftwareControlFlag(path));
    }

    public void Dispose()
    {
        Flag.Clear();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
