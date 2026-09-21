namespace AutoFan.Core;

/// <summary>
/// Fan-test dwell: temperatures have stopped moving. Not a kitchen timer.
/// </summary>
public static class TemperatureSettle
{
    public static bool IsSettled(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < ThermalDynamics.SettleWindowSamples)
        {
            return false;
        }

        IReadOnlyList<double> window = Tail(values, ThermalDynamics.SettleWindowSamples);
        return window.Max() - window.Min() <= ThermalDynamics.SettleBandCelsius * 2;
    }

    public static bool RelevantTempsSettled(IReadOnlyList<HardwareSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count < ThermalDynamics.SettleWindowSamples)
        {
            return false;
        }

        IReadOnlyList<HardwareSnapshot> window = Tail(snapshots, ThermalDynamics.SettleWindowSamples);
        IReadOnlyList<double> cpu = Read(window, SensorKind.CpuTemperature);
        IReadOnlyList<double> gpu = Read(window, SensorKind.GpuTemperature);
        bool cpuReady = cpu.Count == 0 || IsSettled(cpu);
        bool gpuReady = gpu.Count == 0 || IsSettled(gpu);
        return cpuReady && gpuReady && (cpu.Count > 0 || gpu.Count > 0);
    }

    private static IReadOnlyList<double> Read(
        IReadOnlyList<HardwareSnapshot> snapshots,
        SensorKind kind)
    {
        var values = new List<double>();
        foreach (HardwareSnapshot snapshot in snapshots)
        {
            if (PreferredTemperature.Read(snapshot, kind) is double value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static IReadOnlyList<T> Tail<T>(IReadOnlyList<T> values, int count)
    {
        if (values.Count <= count)
        {
            return values;
        }

        return values.Skip(values.Count - count).ToArray();
    }
}
