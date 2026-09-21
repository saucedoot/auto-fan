namespace AutoFan.Core;

/// <summary>
/// Start condition for heating experiments. This is not an abort ceiling and
/// must stay below <see cref="SafetyLimits"/>.
/// </summary>
public static class ExperimentStartGate
{
    public const int CpuReadyCelsius = 60;
    public const int GpuReadyCelsius = 60;

    public static TimeSpan Timeout { get; } = TimeSpan.FromMinutes(5);

    public static bool IsReady(HardwareSnapshot snapshot, out string detail)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        double? cpu = PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature);
        if (cpu > CpuReadyCelsius)
        {
            detail = $"Waiting for the CPU to cool to {CpuReadyCelsius} °C or below (now {cpu:0.#} °C). Not heating yet. Fans still on BIOS.";
            return false;
        }

        double? gpu = PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature);
        if (gpu > GpuReadyCelsius)
        {
            detail = $"Waiting for the GPU to cool to {GpuReadyCelsius} °C or below (now {gpu:0.#} °C). Not heating yet. Fans still on BIOS.";
            return false;
        }

        detail = "Cool enough to start. Light heat comes next. Fans still on BIOS.";
        return true;
    }
}
