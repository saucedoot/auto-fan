namespace AutoFan.Core;

public static class PreferredTemperature
{
    public static double? Read(HardwareSnapshot snapshot, SensorKind kind)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SensorReading[] matches = snapshot.Sensors
            .Where(sensor => sensor.Kind == kind && sensor.Value is double)
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        if (kind == SensorKind.GpuTemperature)
        {
            SensorReading? core = matches.FirstOrDefault(static sensor => IsGpuCoreName(sensor.Name));
            if (core?.Value is double gpuCore)
            {
                return gpuCore;
            }

            SensorReading? fallback = matches.FirstOrDefault(static sensor => !IsGpuSecondaryName(sensor.Name));
            return (fallback ?? matches[0]).Value;
        }

        if (kind == SensorKind.CpuTemperature)
        {
            SensorReading? package = matches.FirstOrDefault(static sensor =>
                Contains(sensor.Name, "tctl")
                || Contains(sensor.Name, "tdie")
                || Contains(sensor.Name, "package"));
            if (package?.Value is double cpu)
            {
                return cpu;
            }
        }

        return matches[0].Value;
    }

    public static double? Read(HardwareSnapshot snapshot, InfluenceTarget target) =>
        Read(snapshot, KindOf(target));

    public static SensorKind KindOf(InfluenceTarget target) =>
        target switch
        {
            InfluenceTarget.Cpu => SensorKind.CpuTemperature,
            InfluenceTarget.Gpu => SensorKind.GpuTemperature,
            InfluenceTarget.Vrm => SensorKind.VrmTemperature,
            InfluenceTarget.Case => SensorKind.CaseTemperature,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, message: null),
        };

    private static bool IsGpuCoreName(string name) =>
        !IsGpuSecondaryName(name) && (Contains(name, "core") || name.Equals("GPU", StringComparison.OrdinalIgnoreCase));

    private static bool IsGpuSecondaryName(string name) =>
        Contains(name, "vr")
        || Contains(name, "soc")
        || Contains(name, "hotspot")
        || Contains(name, "hot spot")
        || Contains(name, "memory");

    private static bool Contains(string name, string token) =>
        name.Contains(token, StringComparison.OrdinalIgnoreCase);
}
