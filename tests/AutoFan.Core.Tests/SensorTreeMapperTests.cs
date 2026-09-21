using System.Text.Json;
using System.Text.Json.Serialization;
using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class SensorTreeMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Map_classifies_temperatures_and_power_from_the_recorded_tree()
    {
        MappedHardware mapped = MapFixture();

        Assert.False(mapped.Snapshot.IsDemoHardware);
        Assert.Equal("ASUS ROG STRIX Z790-E", mapped.Identity.DisplayName);
        Assert.Equal("Intel Core i7-13700K", mapped.Identity.CpuName);
        Assert.Equal("NVIDIA GeForce RTX 4070", mapped.Identity.GpuName);
        Assert.Equal("ASUS ROG STRIX Z790-E", mapped.Identity.MotherboardName);

        Assert.Equal(SensorKind.CpuTemperature, KindOf(mapped, "/cpu/0/temperature/0"));
        Assert.Equal(SensorKind.OtherTemperature, KindOf(mapped, "/cpu/0/temperature/1"));
        Assert.Equal(SensorKind.CpuTemperature, KindOf(mapped, "/cpu/0/temperature/2"));
        Assert.Equal(SensorKind.CpuPower, KindOf(mapped, "/cpu/0/power/0"));
        Assert.Equal(SensorKind.CpuClock, KindOf(mapped, "/cpu/0/clock/0"));
        Assert.Equal(SensorKind.CpuLoad, KindOf(mapped, "/cpu/0/load/0"));
        Assert.Equal(SensorKind.GpuTemperature, KindOf(mapped, "/gpu/0/temperature/0"));
        Assert.Equal(SensorKind.OtherTemperature, KindOf(mapped, "/gpu/0/temperature/1"));
        Assert.Equal(SensorKind.GpuPower, KindOf(mapped, "/gpu/0/power/0"));
        Assert.Equal(SensorKind.GpuClock, KindOf(mapped, "/gpu/0/clock/0"));
        Assert.Equal(SensorKind.GpuLoad, KindOf(mapped, "/gpu/0/load/0"));
        Assert.Equal(SensorKind.AmbientTemperature, KindOf(mapped, "/motherboard/0/temperature/0"));
        Assert.Equal(SensorKind.VrmTemperature, KindOf(mapped, "/motherboard/0/temperature/1"));
        Assert.Equal(SensorKind.MotherboardTemperature, KindOf(mapped, "/motherboard/0/temperature/2"));
        Assert.Equal(SensorKind.CaseTemperature, KindOf(mapped, "/motherboard/0/temperature/3"));
        Assert.Equal(SensorKind.MotherboardTemperature, KindOf(mapped, "/motherboard/0/temperature/4"));
        Assert.Null(mapped.Snapshot.Sensors.Single(s => s.Id == "/motherboard/0/temperature/4").Value);
    }

    [Fact]
    public void Map_pairs_controls_with_fans_and_marks_pumps_and_read_only_headers()
    {
        MappedHardware mapped = MapFixture();
        IReadOnlyList<FanGroup> groups = mapped.Snapshot.FanGroups;

        FanGroup gpu = groups.Single(g => g.Id == "/gpu/0/control/0");
        Assert.Equal("GPU Fan", gpu.Name);
        Assert.Equal("NVIDIA GeForce RTX 4070", gpu.ControllerName);
        Assert.True(gpu.IsControllable);
        Assert.Equal(FanGroupKind.Fan, gpu.Kind);
        Assert.Equal(40, gpu.DutyCyclePercent);
        Assert.Equal(1100, gpu.Rpm);

        FanGroup front = groups.Single(g => g.Id == "/lpc/nct6798d/control/1");
        Assert.Equal("Fan #1", front.Name);
        Assert.Equal("Nuvoton NCT6798D", front.ControllerName);
        Assert.True(front.IsControllable);
        Assert.Equal(35, front.DutyCyclePercent);
        Assert.Equal(820, front.Rpm);

        FanGroup pump = groups.Single(g => g.Id == "/lpc/nct6798d/control/2");
        Assert.Equal(FanGroupKind.Pump, pump.Kind);
        Assert.Equal("Pump", pump.Name);
        Assert.True(pump.IsControllable);
        Assert.Equal(60, pump.DutyCyclePercent);
        Assert.Equal(2400, pump.Rpm);
        Assert.Equal(SensorKind.PumpRpm, KindOf(mapped, "/lpc/nct6798d/control/2-rpm"));

        FanGroup readOnly = groups.Single(g => g.Id == "/lpc/nct6798d/fan/3");
        Assert.False(readOnly.IsControllable);
        Assert.Null(readOnly.DutyCyclePercent);
        Assert.Equal(500, readOnly.Rpm);
        Assert.Equal(FanGroupKind.Fan, readOnly.Kind);
    }

    [Fact]
    public void Map_does_not_invent_case_layout_labels()
    {
        MappedHardware mapped = MapFixture();

        Assert.DoesNotContain(mapped.Snapshot.FanGroups, group =>
            group.Name.Contains("intake", StringComparison.OrdinalIgnoreCase)
            || group.Name.Contains("exhaust", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Map_omits_clock_and_load_that_are_not_cpu_or_gpu()
    {
        var tree = new SensorTree(
        [
            new HardwareNode(
                "/motherboard/0",
                "Board",
                HardwareRole.Motherboard,
                [
                    new RawSensor("/motherboard/0/clock/0", "BUS", RawSensorType.Clock, 100, 0, false),
                    new RawSensor("/motherboard/0/load/0", "Memory", RawSensorType.Load, 40, 0, false),
                ],
                []),
        ]);

        MappedHardware mapped = SensorTreeMapper.Map(tree);

        Assert.Empty(mapped.Snapshot.Sensors);
    }

    [Fact]
    public void Map_defaults_to_the_discrete_gpu_and_can_follow_a_saved_pick()
    {
        var tree = new SensorTree(
        [
            new HardwareNode(
                "/gpu/igpu",
                "Intel UHD Graphics 770",
                HardwareRole.Gpu,
                [
                    new RawSensor("/gpu/igpu/temperature/0", "GPU Core", RawSensorType.Temperature, 41, 0, false),
                    new RawSensor("/gpu/igpu/power/0", "GPU Package", RawSensorType.Power, 12, 0, false),
                ],
                []),
            new HardwareNode(
                "/gpu/dgpu",
                "NVIDIA GeForce RTX 4070",
                HardwareRole.Gpu,
                [
                    new RawSensor("/gpu/dgpu/temperature/0", "GPU Core", RawSensorType.Temperature, 52, 0, false),
                    new RawSensor("/gpu/dgpu/power/0", "GPU Package", RawSensorType.Power, 140, 0, false),
                ],
                []),
        ]);

        MappedHardware automatic = SensorTreeMapper.Map(tree);
        Assert.Equal("NVIDIA GeForce RTX 4070", automatic.Identity.GpuName);
        Assert.Equal(2, automatic.AvailableGpus.Count);
        Assert.False(automatic.AvailableGpus[0].LooksDiscrete);
        Assert.True(automatic.AvailableGpus[1].LooksDiscrete);
        Assert.Contains(automatic.Snapshot.Sensors, sensor => sensor.Id == "/gpu/dgpu/temperature/0");
        Assert.DoesNotContain(automatic.Snapshot.Sensors, sensor => sensor.Id == "/gpu/igpu/temperature/0");

        MappedHardware picked = SensorTreeMapper.Map(tree, preferredGpuId: "/gpu/igpu");
        Assert.Equal("Intel UHD Graphics 770", picked.Identity.GpuName);
        Assert.Contains(picked.Snapshot.Sensors, sensor => sensor.Id == "/gpu/igpu/power/0");
        Assert.DoesNotContain(picked.Snapshot.Sensors, sensor => sensor.Id == "/gpu/dgpu/power/0");
    }

    [Fact]
    public void Map_does_not_treat_fan_number_one_as_a_pump_just_because_a_flow_sensor_shares_the_index()
    {
        var tree = new SensorTree(
        [
            new HardwareNode(
                "/lpc/nct6687d",
                "Nuvoton NCT6687D",
                HardwareRole.SuperIo,
                [
                    new RawSensor("/lpc/nct6687d/fan/1", "Fan #1", RawSensorType.Fan, 900, 1, true),
                    new RawSensor("/lpc/nct6687d/flow/1", "Fan #1", RawSensorType.Flow, 0, 1, false),
                    new RawSensor("/lpc/nct6687d/control/1", "Fan Control #1", RawSensorType.Control, 40, 1, true),
                ],
                []),
        ]);

        MappedHardware mapped = SensorTreeMapper.Map(tree);
        FanGroup group = Assert.Single(mapped.Snapshot.FanGroups);

        Assert.Equal("Fan #1", group.Name);
        Assert.Equal(FanGroupKind.Fan, group.Kind);
        Assert.True(group.IsControllable);
    }

    [Theory]
    [InlineData("Fan #1", false)]
    [InlineData("Pump Fan #1", false)]
    [InlineData("Pump Fan", false)]
    [InlineData("Pump", true)]
    [InlineData("AIO Pump", true)]
    [InlineData("W_PUMP", true)]
    public void LooksLikePump_ignores_numbered_and_pump_fan_headers(string name, bool expected)
    {
        Assert.Equal(expected, SensorTreeMapper.LooksLikePump(name));
    }

    [Fact]
    public void Map_keeps_null_values_and_empty_trees()
    {
        var tree = new SensorTree(
        [
            new HardwareNode(
                "/cpu/0",
                "Unknown CPU",
                HardwareRole.Cpu,
                [new RawSensor("/cpu/0/temperature/0", "CPU Package", RawSensorType.Temperature, null, 0, false)],
                []),
        ]);

        MappedHardware mapped = SensorTreeMapper.Map(tree);

        Assert.Equal("This PC", mapped.Identity.DisplayName);
        Assert.Null(mapped.Identity.MotherboardName);
        Assert.Null(mapped.Snapshot.Sensors.Single().Value);
        Assert.Empty(mapped.Snapshot.FanGroups);
    }

    internal static MappedHardware MapFixture() => SensorTreeMapper.Map(LoadFixture());

    internal static SensorTree LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-sensor-tree.json");
        string json = File.ReadAllText(path);
        SensorTree? tree = JsonSerializer.Deserialize<SensorTree>(json, JsonOptions);
        Assert.NotNull(tree);
        return tree;
    }

    private static SensorKind KindOf(MappedHardware mapped, string id) =>
        mapped.Snapshot.Sensors.Single(sensor => sensor.Id == id).Kind;
}
