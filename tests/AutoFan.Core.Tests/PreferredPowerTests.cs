using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class PreferredPowerTests
{
    [Fact]
    public void Prefers_a_package_reading_when_several_exist()
    {
        var snapshot = new HardwareSnapshot(
            DateTimeOffset.UtcNow,
            [
                new SensorReading("gpu-board", "GPU Board", SensorKind.GpuPower, 40, "W"),
                new SensorReading("gpu-package", "GPU Package", SensorKind.GpuPower, 88, "W"),
            ],
            [],
            IsDemoHardware: true);

        Assert.Equal(88, PreferredPower.Read(snapshot, SensorKind.GpuPower));
    }

    [Fact]
    public void Missing_power_is_unknown()
    {
        var snapshot = new HardwareSnapshot(DateTimeOffset.UtcNow, [], [], IsDemoHardware: true);

        Assert.Null(PreferredPower.Read(snapshot, SensorKind.CpuPower));
    }

    [Fact]
    public void Rejects_a_temperature_kind()
    {
        var hardware = new FakeHardwareBackend();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreferredPower.Read(hardware.ReadSnapshot(), SensorKind.CpuTemperature));
    }
}
