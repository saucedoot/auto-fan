using System.Diagnostics;

namespace AutoFan.Hardware;

public sealed class ProcessControlWatchdog : IControlWatchdog
{
    private Process? _child;

    public void Arm()
    {
        if (_child is { HasExited: false })
        {
            return;
        }

        string? exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        _child = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"{WatchdogLoop.WatchdogArgument} {WatchdogLoop.ParentArgument} {Environment.ProcessId}",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    public void Disarm()
    {
        Process? child = _child;
        _child = null;
        if (child is null)
        {
            return;
        }

        try
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: false);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        finally
        {
            child.Dispose();
        }
    }
}
