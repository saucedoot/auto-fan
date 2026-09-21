using LibreHardwareMonitor.Hardware;

namespace AutoFan.Hardware;

/// <summary>
/// Best-effort return of motherboard headers to BIOS and NVIDIA GPU fans to
/// the driver curve. Used by crash recovery and the next-launch dirty path.
/// </summary>
public static class BiosFanRestorer
{
    public static void RestoreAll()
    {
        RestoreMotherboard();
        NvidiaFanWriter.RestoreAll();
    }

    private static void RestoreMotherboard()
    {
        Computer? computer = null;
        try
        {
            computer = new Computer
            {
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
            };
            computer.Open();
            computer.Accept(new UpdateVisitor());
            foreach (IControl control in ControllableHeaders.WritableControls(computer))
            {
                try
                {
                    control.SetDefault();
                }
                catch (Exception)
                {
                    // Keep restoring the remaining headers.
                }
            }
        }
        catch (Exception)
        {
            // Watchdog and startup restore must not crash the process.
        }
        finally
        {
            computer?.Close();
        }
    }
}
