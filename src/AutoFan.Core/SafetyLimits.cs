namespace AutoFan.Core;

/// <summary>
/// Hardcoded safety ceilings. These are not user settings and must not be
/// loosened by configuration, UI, or later phases. User abort fields may only
/// stop tests sooner.
/// </summary>
public static class SafetyLimits
{
    public const int CpuAbortCelsius = 90;
    public const int GpuAbortCelsius = 83;
    public const int OtherThermalAbortCelsius = 95;
    public const int MinDutyPercent = 0;
    public const int MaxDutyPercent = 100;
    public const int MaxExperimentDurationMinutes = 30;

    public static bool IsDutyInRange(int percent) =>
        percent >= MinDutyPercent && percent <= MaxDutyPercent;

    public static ThermalAbortReason? Evaluate(
        HardwareSnapshot snapshot,
        ThermalAbortLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ThermalAbortLimits effective = Effective(limits);
        foreach (SensorReading sensor in snapshot.Sensors)
        {
            if (sensor.Value is not double value)
            {
                continue;
            }

            switch (sensor.Kind)
            {
                case SensorKind.CpuTemperature when value >= effective.CpuCelsius:
                    return ThermalAbortReason.CpuOverLimit;
                case SensorKind.GpuTemperature when value >= effective.GpuCelsius:
                    return ThermalAbortReason.GpuOverLimit;
                case SensorKind.VrmTemperature:
                case SensorKind.MotherboardTemperature:
                case SensorKind.CaseTemperature:
                case SensorKind.OtherTemperature:
                    if (value >= OtherThermalAbortCelsius)
                    {
                        return ThermalAbortReason.OtherSensorOverLimit;
                    }

                    break;
            }
        }

        return null;
    }

    public static ThermalAbortReason? EvaluateWhileHeating(
        HardwareSnapshot snapshot,
        IWorkloadActuator workload,
        ThermalAbortLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(workload);

        if (workload.HasFault)
        {
            return ThermalAbortReason.GpuDeviceLost;
        }

        ThermalAbortReason? ceiling = Evaluate(snapshot, limits);
        if (ceiling is not null)
        {
            return ceiling;
        }

        if (PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature) is null)
        {
            return ThermalAbortReason.TelemetryLost;
        }

        if (workload.GpuLoadAvailable
            && PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature) is null)
        {
            return ThermalAbortReason.TelemetryLost;
        }

        return null;
    }

    public static string Describe(ThermalAbortReason reason, ThermalAbortLimits? limits = null)
    {
        ThermalAbortLimits effective = Effective(limits);
        return reason switch
        {
            ThermalAbortReason.CpuOverLimit =>
                $"CPU temperature reached the {effective.CpuCelsius:0} °C abort limit.",
            ThermalAbortReason.GpuOverLimit =>
                $"GPU temperature reached the {effective.GpuCelsius:0} °C abort limit.",
            ThermalAbortReason.OtherSensorOverLimit =>
                $"A thermal sensor reached the {OtherThermalAbortCelsius} °C abort limit.",
            ThermalAbortReason.RateOfRise =>
                "Temperature rose too quickly. Stopping before it hits the abort limit.",
            ThermalAbortReason.TelemetryLost =>
                "A needed temperature sensor stopped reporting. Stopping the heat test.",
            ThermalAbortReason.GpuDeviceLost =>
                "The GPU reset or the graphics driver stopped. Stopping the heat test.",
            _ => "Thermal abort limit reached.",
        };
    }

    public static ThermalAbortReason? TryParse(string? detail, ThermalAbortLimits? limits = null)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        foreach (ThermalAbortReason reason in Enum.GetValues<ThermalAbortReason>())
        {
            if (string.Equals(detail, Describe(reason, limits), StringComparison.Ordinal))
            {
                return reason;
            }
        }

        if (detail.Contains("too quickly", StringComparison.Ordinal))
        {
            return ThermalAbortReason.RateOfRise;
        }

        if (detail.Contains("stopped reporting", StringComparison.Ordinal))
        {
            return ThermalAbortReason.TelemetryLost;
        }

        if (detail.Contains("GPU reset", StringComparison.Ordinal))
        {
            return ThermalAbortReason.GpuDeviceLost;
        }

        if (detail.Contains("CPU temperature reached", StringComparison.Ordinal))
        {
            return ThermalAbortReason.CpuOverLimit;
        }

        if (detail.Contains("GPU temperature reached", StringComparison.Ordinal))
        {
            return ThermalAbortReason.GpuOverLimit;
        }

        if (detail.Contains("thermal sensor reached", StringComparison.Ordinal))
        {
            return ThermalAbortReason.OtherSensorOverLimit;
        }

        return null;
    }

    private static ThermalAbortLimits Effective(ThermalAbortLimits? limits)
    {
        if (limits is not ThermalAbortLimits value)
        {
            return ThermalAbortLimits.Floor;
        }

        return ThermalAbortLimits.FromUser(value.CpuCelsius, value.GpuCelsius);
    }
}
