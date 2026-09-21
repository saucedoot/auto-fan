namespace AutoFan.Hardware;

/// <summary>
/// Pairs a software-control write with the dirty flag and watchdog.
/// </summary>
public sealed class SoftwareControlLease
{
    private readonly SoftwareControlFlag _flag;
    private readonly IControlWatchdog _watchdog;

    public SoftwareControlLease()
        : this(new SoftwareControlFlag(), new ProcessControlWatchdog())
    {
    }

    public SoftwareControlLease(SoftwareControlFlag flag, IControlWatchdog watchdog)
    {
        _flag = flag ?? throw new ArgumentNullException(nameof(flag));
        _watchdog = watchdog ?? throw new ArgumentNullException(nameof(watchdog));
    }

    public SoftwareControlFlag Flag => _flag;

    public void OnTakingControl()
    {
        _flag.MarkActive();
        _watchdog.Arm();
    }

    public void OnRestored()
    {
        _flag.Clear();
        _watchdog.Disarm();
    }
}
