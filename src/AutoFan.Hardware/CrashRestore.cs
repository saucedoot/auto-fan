namespace AutoFan.Hardware;

/// <summary>
/// Restores BIOS fan control when a previous session died while still holding
/// software control.
/// </summary>
public static class CrashRestore
{
    public static bool RestoreIfDirty(SoftwareControlFlag flag, Action restore)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentNullException.ThrowIfNull(restore);

        if (!flag.IsActive)
        {
            return false;
        }

        restore();
        flag.Clear();
        return true;
    }
}
