namespace AutoFan.Hardware;

public sealed class NullControlWatchdog : IControlWatchdog
{
    public static NullControlWatchdog Instance { get; } = new();

    public void Arm()
    {
    }

    public void Disarm()
    {
    }
}
