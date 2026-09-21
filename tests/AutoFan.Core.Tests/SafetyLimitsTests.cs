using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class SafetyLimitsTests
{
    [Fact]
    public void Hardcoded_ceilings_match_the_agreed_P0_limits()
    {
        Assert.Equal(90, SafetyLimits.CpuAbortCelsius);
        Assert.Equal(83, SafetyLimits.GpuAbortCelsius);
        Assert.Equal(95, SafetyLimits.OtherThermalAbortCelsius);
        Assert.Equal(0, SafetyLimits.MinDutyPercent);
        Assert.Equal(100, SafetyLimits.MaxDutyPercent);
        Assert.Equal(30, SafetyLimits.MaxExperimentDurationMinutes);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(50, true)]
    [InlineData(100, true)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, false)]
    public void Duty_range_rejects_values_outside_0_to_100(int percent, bool expected)
    {
        Assert.Equal(expected, SafetyLimits.IsDutyInRange(percent));
    }

    [Fact]
    public void Evaluate_aborts_when_cpu_is_at_the_ceiling()
    {
        HardwareSnapshot snapshot = SnapshotWith(SensorKind.CpuTemperature, SafetyLimits.CpuAbortCelsius);
        Assert.Equal(ThermalAbortReason.CpuOverLimit, SafetyLimits.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_aborts_when_gpu_is_at_the_ceiling()
    {
        HardwareSnapshot snapshot = SnapshotWith(SensorKind.GpuTemperature, SafetyLimits.GpuAbortCelsius);
        Assert.Equal(ThermalAbortReason.GpuOverLimit, SafetyLimits.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_aborts_when_other_temperature_is_at_the_ceiling()
    {
        HardwareSnapshot snapshot = SnapshotWith(SensorKind.OtherTemperature, SafetyLimits.OtherThermalAbortCelsius);
        Assert.Equal(ThermalAbortReason.OtherSensorOverLimit, SafetyLimits.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_does_not_abort_on_hot_ambient()
    {
        HardwareSnapshot snapshot = SnapshotWith(SensorKind.AmbientTemperature, SafetyLimits.OtherThermalAbortCelsius + 5);
        Assert.Null(SafetyLimits.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_allows_demo_idle_temperatures()
    {
        HardwareSnapshot snapshot = SnapshotWith(SensorKind.CpuTemperature, 45, extraGpu: 42);
        Assert.Null(SafetyLimits.Evaluate(snapshot));
    }

    [Fact]
    public void EvaluateWhileHeating_aborts_when_cpu_temperature_is_missing()
    {
        HardwareSnapshot snapshot = new(
            DateTimeOffset.UtcNow,
            [new SensorReading("gpu-temp", "GPU", SensorKind.GpuTemperature, 42, "°C")],
            [],
            IsDemoHardware: true);
        var workload = new FakeWorkloadActuator();
        Assert.Equal(
            ThermalAbortReason.TelemetryLost,
            SafetyLimits.EvaluateWhileHeating(snapshot, workload));
    }

    [Fact]
    public void EvaluateWhileHeating_aborts_when_gpu_heat_is_on_and_gpu_temperature_is_missing()
    {
        HardwareSnapshot snapshot = WithoutGpu(SnapshotWith(SensorKind.CpuTemperature, 45, extraGpu: 42));
        var workload = new FakeWorkloadActuator { GpuLoadAvailable = true };
        Assert.Equal(
            ThermalAbortReason.TelemetryLost,
            SafetyLimits.EvaluateWhileHeating(snapshot, workload));
    }

    [Fact]
    public void EvaluateWhileHeating_allows_missing_gpu_when_gpu_load_is_unavailable()
    {
        HardwareSnapshot snapshot = WithoutGpu(SnapshotWith(SensorKind.CpuTemperature, 45, extraGpu: 42));
        var workload = new FakeWorkloadActuator { GpuLoadAvailable = false };
        Assert.Null(SafetyLimits.EvaluateWhileHeating(snapshot, workload));
    }

    [Fact]
    public void EvaluateWhileHeating_aborts_when_the_gpu_device_is_lost()
    {
        HardwareSnapshot snapshot = SnapshotWith(SensorKind.CpuTemperature, 45, extraGpu: 42);
        var workload = new FakeWorkloadActuator { HasFault = true };
        Assert.Equal(
            ThermalAbortReason.GpuDeviceLost,
            SafetyLimits.EvaluateWhileHeating(snapshot, workload));
        Assert.Contains("GPU reset", SafetyLimits.Describe(ThermalAbortReason.GpuDeviceLost), StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_reads_the_abort_sentences()
    {
        Assert.Equal(
            ThermalAbortReason.RateOfRise,
            SafetyLimits.TryParse(SafetyLimits.Describe(ThermalAbortReason.RateOfRise)));
        Assert.Equal(
            ThermalAbortReason.TelemetryLost,
            SafetyLimits.TryParse(SafetyLimits.Describe(ThermalAbortReason.TelemetryLost)));
        Assert.Equal(
            ThermalAbortReason.GpuDeviceLost,
            SafetyLimits.TryParse("The GPU reset or the graphics driver stopped. Stopping the heat test."));
        Assert.Null(SafetyLimits.TryParse("Competing fan software is running: FanControl."));
        Assert.Null(SafetyLimits.TryParse(null));
    }

    private static HardwareSnapshot WithoutGpu(HardwareSnapshot snapshot) =>
        new(
            snapshot.CapturedAt,
            snapshot.Sensors.Where(sensor => sensor.Kind != SensorKind.GpuTemperature).ToArray(),
            snapshot.FanGroups,
            snapshot.IsDemoHardware);

    private static HardwareSnapshot SnapshotWith(SensorKind kind, double value, double extraGpu = 40)
    {
        var sensors = new List<SensorReading>
        {
            new SensorReading("cpu-temp", "CPU", kind == SensorKind.CpuTemperature ? kind : SensorKind.CpuTemperature, kind == SensorKind.CpuTemperature ? value : 45, "°C"),
            new SensorReading("gpu-temp", "GPU", SensorKind.GpuTemperature, kind == SensorKind.GpuTemperature ? value : extraGpu, "°C"),
        };

        if (kind is SensorKind.VrmTemperature
            or SensorKind.MotherboardTemperature
            or SensorKind.CaseTemperature
            or SensorKind.OtherTemperature
            or SensorKind.AmbientTemperature)
        {
            sensors.Add(new SensorReading("other-temp", "Other", kind, value, "°C"));
        }

        return new HardwareSnapshot(DateTimeOffset.UtcNow, sensors, Array.Empty<FanGroup>(), IsDemoHardware: true);
    }
}
