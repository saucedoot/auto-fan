namespace AutoFan.Core;

/// <summary>
/// Detects a sudden temperature climb near an abort ceiling so we can stop
/// before the limit. An expected climb from a cool start is not an abort.
/// </summary>
public sealed class ThermalTrend
{
    public const double RiseAbortCelsiusPerSecond = 3.0;

    public const double RiseAbortArmBelowCelsius = 5.0;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private readonly List<TrendPoint> _points = [];

    public void Add(DateTimeOffset at, HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        double? other = HottestOther(snapshot);
        _points.Add(new TrendPoint(
            at,
            PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature),
            PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature),
            other));
        Prune(at);
    }

    public ThermalAbortReason? Evaluate() => Evaluate(ThermalAbortLimits.Floor);

    public ThermalAbortReason? Evaluate(ThermalAbortLimits limits)
    {
        if (ArmedRate(static point => point.Cpu, limits.CpuCelsius) is double cpu
            && cpu >= RiseAbortCelsiusPerSecond)
        {
            return ThermalAbortReason.RateOfRise;
        }

        if (ArmedRate(static point => point.Gpu, limits.GpuCelsius) is double gpu
            && gpu >= RiseAbortCelsiusPerSecond)
        {
            return ThermalAbortReason.RateOfRise;
        }

        if (ArmedRate(static point => point.Other, SafetyLimits.OtherThermalAbortCelsius) is double other
            && other >= RiseAbortCelsiusPerSecond)
        {
            return ThermalAbortReason.RateOfRise;
        }

        return null;
    }

    private double? ArmedRate(Func<TrendPoint, double?> read, double abortCelsius)
    {
        double? last = Last(read);
        if (last is not double value || value < abortCelsius - RiseAbortArmBelowCelsius)
        {
            return null;
        }

        return Rate(read);
    }

    private double? Last(Func<TrendPoint, double?> read)
    {
        double? value = null;
        foreach (TrendPoint point in _points)
        {
            if (read(point) is double current)
            {
                value = current;
            }
        }

        return value;
    }

    private double? Rate(Func<TrendPoint, double?> read)
    {
        TrendPoint? first = null;
        TrendPoint? last = null;
        foreach (TrendPoint point in _points)
        {
            if (read(point) is null)
            {
                continue;
            }

            first ??= point;
            last = point;
        }

        if (first is null || last is null || first.Value.At == last.Value.At)
        {
            return null;
        }

        double seconds = (last.Value.At - first.Value.At).TotalSeconds;
        if (seconds < 1)
        {
            return null;
        }

        double start = read(first.Value)!.Value;
        double end = read(last.Value)!.Value;
        return (end - start) / seconds;
    }

    private void Prune(DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - Window;
        _points.RemoveAll(point => point.At < cutoff);
    }

    private static double? HottestOther(HardwareSnapshot snapshot)
    {
        double? hottest = null;
        foreach (SensorReading sensor in snapshot.Sensors)
        {
            if (sensor.Value is not double value)
            {
                continue;
            }

            if (sensor.Kind is not (SensorKind.VrmTemperature
                or SensorKind.MotherboardTemperature
                or SensorKind.CaseTemperature
                or SensorKind.OtherTemperature))
            {
                continue;
            }

            if (hottest is null || value > hottest)
            {
                hottest = value;
            }
        }

        return hottest;
    }

    private readonly record struct TrendPoint(
        DateTimeOffset At,
        double? Cpu,
        double? Gpu,
        double? Other);
}
