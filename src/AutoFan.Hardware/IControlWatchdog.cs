namespace AutoFan.Hardware;

/// <summary>
/// Arms crash recovery while software fan control is held.
/// </summary>
public interface IControlWatchdog
{
    void Arm();

    void Disarm();
}
