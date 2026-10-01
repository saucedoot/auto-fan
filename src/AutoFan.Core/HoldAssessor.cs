namespace AutoFan.Core;

/// <summary>
/// Decides whether a hold is evidence. The numbers are provisional until a
/// trace from this PC is reviewed. They are not a user setting.
/// </summary>
public static class HoldAssessor
{
    public const int MinimumWindowSamples = 20;

    public const double MinimumWindowSeconds = 15;

    public const double MaxWindowRangeCelsius = 0.5;

    public const double MaxSlopeCelsiusPerSecond = 0.02;

    public const double MaxPowerChangeFraction = 0.08;

    public const double MaxPowerChangeWatts = 5;

    public const double MaxLoadChangePercent = 15;

    public const double MaxClockDropFraction = 0.05;

    public static HoldAssessment Evaluate(
        IReadOnlyList<HardwareSnapshot> snapshots,
        bool timedOut,
        string? fanGroupId = null,
        bool dutyCommanded = false,
        string? secondFanGroupId = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count == 0 || PreferredTemperature.Read(snapshots[^1], SensorKind.CpuTemperature) is null)
        {
            return timedOut ? HoldAssessment.TimedOut : HoldAssessment.TelemetryLost;
        }

        if (dutyCommanded
            && (Stopped(snapshots[^1], fanGroupId) || Stopped(snapshots[^1], secondFanGroupId)))
        {
            return HoldAssessment.FanStalled;
        }

        if (!WindowReady(snapshots))
        {
            return timedOut ? HoldAssessment.TimedOut : HoldAssessment.TransientModeled;
        }

        if (!TemperaturesQuiet(snapshots))
        {
            return timedOut ? HoldAssessment.TimedOut : HoldAssessment.TransientModeled;
        }

        if (PreferredPower.Read(snapshots[^1], SensorKind.CpuPower) is null)
        {
            return timedOut ? HoldAssessment.TimedOut : HoldAssessment.TransientModeled;
        }

        if (HasGpuTemperature(snapshots[^1])
            && PreferredPower.Read(snapshots[^1], SensorKind.GpuPower) is null)
        {
            return timedOut ? HoldAssessment.TimedOut : HoldAssessment.TransientModeled;
        }

        if (!PowerStable(snapshots, SensorKind.CpuPower)
            || (HasGpuTemperature(snapshots[^1]) && !PowerStable(snapshots, SensorKind.GpuPower))
            || !LoadStable(snapshots, SensorKind.CpuLoad)
            || !LoadStable(snapshots, SensorKind.GpuLoad))
        {
            return HoldAssessment.PowerUnstable;
        }

        if (ClockDropped(snapshots, SensorKind.CpuClock) || ClockDropped(snapshots, SensorKind.GpuClock))
        {
            return HoldAssessment.Throttled;
        }

        return HoldAssessment.SettledMeasured;
    }

    private static bool WindowReady(IReadOnlyList<HardwareSnapshot> snapshots)
    {
        if (snapshots.Count < MinimumWindowSamples)
        {
            return false;
        }

        double seconds = (snapshots[^1].CapturedAt - snapshots[^MinimumWindowSamples].CapturedAt).TotalSeconds;
        return seconds >= MinimumWindowSeconds;
    }

    private static bool TemperaturesQuiet(IReadOnlyList<HardwareSnapshot> snapshots)
    {
        IReadOnlyList<HardwareSnapshot> window = snapshots.Skip(snapshots.Count - MinimumWindowSamples).ToArray();
        return Quiet(window, SensorKind.CpuTemperature)
            && (!HasGpuTemperature(window[^1]) || Quiet(window, SensorKind.GpuTemperature));
    }

    private static bool Quiet(IReadOnlyList<HardwareSnapshot> window, SensorKind kind)
    {
        var values = new List<(DateTimeOffset At, double Value)>();
        foreach (HardwareSnapshot snapshot in window)
        {
            if (PreferredTemperature.Read(snapshot, kind) is double value)
            {
                values.Add((snapshot.CapturedAt, value));
            }
        }

        if (values.Count < MinimumWindowSamples)
        {
            return false;
        }

        double range = values.Max(static point => point.Value) - values.Min(static point => point.Value);
        double seconds = (values[^1].At - values[0].At).TotalSeconds;
        if (seconds <= 0)
        {
            return false;
        }

        double slope = Math.Abs(values[^1].Value - values[0].Value) / seconds;
        return range <= MaxWindowRangeCelsius && slope <= MaxSlopeCelsiusPerSecond;
    }

    private static bool PowerStable(IReadOnlyList<HardwareSnapshot> snapshots, SensorKind kind)
    {
        double? start = PreferredPower.Read(snapshots[0], kind);
        double? end = PreferredPower.Read(snapshots[^1], kind);
        if (start is not double from || end is not double to)
        {
            return false;
        }

        double change = Math.Abs(to - from);
        return change <= MaxPowerChangeWatts || change <= Math.Abs(from) * MaxPowerChangeFraction;
    }

    private static bool LoadStable(IReadOnlyList<HardwareSnapshot> snapshots, SensorKind kind)
    {
        double? start = Read(snapshots[0], kind);
        double? end = Read(snapshots[^1], kind);
        if (start is null || end is null)
        {
            return true;
        }

        return Math.Abs(end.Value - start.Value) <= MaxLoadChangePercent;
    }

    private static bool ClockDropped(IReadOnlyList<HardwareSnapshot> snapshots, SensorKind kind)
    {
        double? start = Read(snapshots[0], kind);
        double? end = Read(snapshots[^1], kind);
        if (start is not > 0 || end is null)
        {
            return false;
        }

        return end.Value < start.Value * (1 - MaxClockDropFraction);
    }

    private static bool HasGpuTemperature(HardwareSnapshot snapshot) =>
        PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature) is not null;

    private static bool Stopped(HardwareSnapshot snapshot, string? fanGroupId) =>
        fanGroupId is not null && Rpm(snapshot, fanGroupId) is not > 0;

    private static double? Rpm(HardwareSnapshot snapshot, string fanGroupId)
    {
        foreach (FanGroup group in snapshot.FanGroups)
        {
            if (string.Equals(group.Id, fanGroupId, StringComparison.Ordinal))
            {
                return group.Rpm;
            }
        }

        return null;
    }

    private static double? Read(HardwareSnapshot snapshot, SensorKind kind)
    {
        foreach (SensorReading sensor in snapshot.Sensors)
        {
            if (sensor.Kind == kind && sensor.Value is double value)
            {
                return value;
            }
        }

        return null;
    }
}
