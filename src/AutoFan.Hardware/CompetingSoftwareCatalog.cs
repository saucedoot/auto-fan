namespace AutoFan.Hardware;

public static class CompetingSoftwareCatalog
{
    internal static readonly (string ProcessName, string DisplayName)[] Entries =
    [
        ("FanControl", "FanControl"),
        ("ArmouryCrate", "Armoury Crate"),
        ("ArmouryCrate.UserSessionHelper", "Armoury Crate"),
        ("iCUE", "iCUE"),
        ("Corsair.Service", "iCUE"),
        ("CorsairService", "iCUE"),
        ("NZXT CAM", "NZXT CAM"),
        ("NZXT-CAM", "NZXT CAM"),
        ("L-Connect", "L-Connect"),
        ("L-Connect 3", "L-Connect"),
        ("LConnect3", "L-Connect"),
        ("SignalRgb", "SignalRGB"),
        ("OpenRGB", "OpenRGB"),
        ("MSIAfterburner", "MSI Afterburner"),
        ("MSI Center", "MSI Center"),
        ("MSI.CentralServer", "MSI Center"),
        ("GCC", "Gigabyte Control Center"),
        ("AppCenter", "Gigabyte Control Center"),
        ("ArgusMonitor", "Argus Monitor"),
        ("SpeedFan", "SpeedFan"),
        ("AISuite3", "AI Suite"),
        ("ASUSAISuite3", "AI Suite"),
    ];

    public static IReadOnlyList<string> Match(IEnumerable<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);

        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string name in processNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            foreach ((string processName, string displayName) in Entries)
            {
                if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(displayName);
                }
            }
        }

        return [.. found];
    }
}
