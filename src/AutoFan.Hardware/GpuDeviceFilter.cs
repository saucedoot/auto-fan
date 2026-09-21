namespace AutoFan.Hardware;

internal static class GpuDeviceFilter
{
    public static bool IsSoftwareAdapter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        return name.Contains("WARP", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Microsoft Basic Render", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Software Adapter", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksDiscrete(string? name)
    {
        if (IsSoftwareAdapter(name) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.Contains("UHD", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Iris", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase)
            || (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("Arc", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
            || name.Contains("RTX", StringComparison.OrdinalIgnoreCase)
            || name.Contains("GTX", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
            || name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
    }
}
