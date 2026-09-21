using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class TemperatureSettleTests
{
    [Fact]
    public void Still_moving_series_is_not_settled()
    {
        Assert.False(TemperatureSettle.IsSettled([70, 71, 72, 73, 74]));
        Assert.False(TemperatureSettle.RelevantTempsSettled(
        [
            Snapshot(70, 60),
            Snapshot(71, 61),
            Snapshot(72, 62),
            Snapshot(73, 63),
            Snapshot(74, 64),
        ]));
    }

    [Fact]
    public void Flat_window_is_settled()
    {
        Assert.True(TemperatureSettle.IsSettled([70.1, 70.2, 70.0, 70.3, 70.2]));
        Assert.True(TemperatureSettle.RelevantTempsSettled(
        [
            Snapshot(70.1, 60.1),
            Snapshot(70.2, 60.0),
            Snapshot(70.0, 60.2),
            Snapshot(70.3, 60.1),
            Snapshot(70.2, 60.2),
        ]));
    }

    [Fact]
    public void Too_few_samples_are_not_settled()
    {
        Assert.False(TemperatureSettle.IsSettled([70, 70, 70]));
        Assert.False(TemperatureSettle.RelevantTempsSettled([Snapshot(70, 60), Snapshot(70, 60)]));
    }

    private static HardwareSnapshot Snapshot(double cpu, double gpu) =>
        new(
            DateTimeOffset.UtcNow,
            [
                new SensorReading(FakeHardwareBackend.CpuSensorId, "CPU", SensorKind.CpuTemperature, cpu, "°C"),
                new SensorReading(FakeHardwareBackend.GpuSensorId, "GPU", SensorKind.GpuTemperature, gpu, "°C"),
            ],
            [],
            IsDemoHardware: true);
}
