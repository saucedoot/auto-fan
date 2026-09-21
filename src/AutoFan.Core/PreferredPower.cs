namespace AutoFan.Core;

public static class PreferredPower
{
    public static double? Read(HardwareSnapshot snapshot, SensorKind kind)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (kind is not (SensorKind.CpuPower or SensorKind.GpuPower))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null);
        }

        SensorReading[] matches = snapshot.Sensors
            .Where(sensor => sensor.Kind == kind && sensor.Value is double)
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        SensorReading? package = matches.FirstOrDefault(static sensor =>
            Contains(sensor.Name, "package") || Contains(sensor.Name, "ppt"));
        return (package ?? matches[0]).Value;
    }

    private static bool Contains(string name, string token) =>
        name.Contains(token, StringComparison.OrdinalIgnoreCase);
}
