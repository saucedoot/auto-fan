using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow += delta;

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        return new NoopTimer();
    }

    private sealed class NoopTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class FixedCompetingSoftwareScanner : ICompetingSoftwareScanner
{
    private IReadOnlyList<string> _running;

    public FixedCompetingSoftwareScanner(params string[] running)
    {
        _running = running;
    }

    public void Set(params string[] running) => _running = running;

    public IReadOnlyList<string> DetectRunning() => _running;
}

internal sealed class RecordingWatchdog : IControlWatchdog
{
    public int ArmCount { get; private set; }

    public int DisarmCount { get; private set; }

    public bool IsArmed { get; private set; }

    public void Arm()
    {
        ArmCount++;
        IsArmed = true;
    }

    public void Disarm()
    {
        DisarmCount++;
        IsArmed = false;
    }

    public void SimulateParentDeath(SoftwareControlFlag flag, IHardwareBackend hardware)
    {
        CrashRestore.RestoreIfDirty(flag, hardware.RestoreDefaults);
        Disarm();
    }
}
