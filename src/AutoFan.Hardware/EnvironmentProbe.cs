using System.Security.Principal;
using AutoFan.Core;
using LibreHardwareMonitor.PawnIo;

namespace AutoFan.Hardware;

public static class EnvironmentProbe
{
    public static EnvironmentStatus Check(string? discoveryError = null)
    {
        bool isAdministrator = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        IReadOnlyList<string> competing = new CompetingSoftwareScanner().DetectRunning();
        return EnvironmentStatus.Create(isAdministrator, PawnIo.IsInstalled, discoveryError, competing);
    }
}
