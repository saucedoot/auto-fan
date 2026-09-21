using System.Diagnostics;

namespace AutoFan.Hardware;

/// <summary>
/// Same-exe crash watchdog: wait for the parent, then restore if the dirty
/// flag is still present.
/// </summary>
public static class WatchdogLoop
{
    public const string WatchdogArgument = "--watchdog";
    public const string ParentArgument = "--parent";

    public static bool TryHandle(string[] args)
    {
        if (!TryParseParentPid(args, out int parentPid))
        {
            return false;
        }

        WaitForParent(parentPid);
        CrashRestore.RestoreIfDirty(new SoftwareControlFlag(), BiosFanRestorer.RestoreAll);
        return true;
    }

    public static bool TryParseParentPid(string[] args, out int parentPid)
    {
        parentPid = 0;
        if (args is null || args.Length == 0)
        {
            return false;
        }

        if (!ContainsFlag(args, WatchdogArgument))
        {
            return false;
        }

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], ParentArgument, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out parentPid)
                && parentPid > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsFlag(string[] args, string flag)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void WaitForParent(int parentPid)
    {
        try
        {
            using Process parent = Process.GetProcessById(parentPid);
            parent.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Parent already exited.
        }
    }
}
