using LibreHardwareMonitor.Hardware;

namespace AutoFan.Hardware;

internal static class SensorTreeReader
{
    public static SensorTree FromComputer(IComputer computer)
    {
        ArgumentNullException.ThrowIfNull(computer);
        var nodes = new List<HardwareNode>();
        foreach (IHardware hardware in computer.Hardware)
        {
            nodes.Add(ReadNode(hardware));
        }

        return new SensorTree(nodes);
    }

    private static HardwareNode ReadNode(IHardware hardware)
    {
        var sensors = new List<RawSensor>();
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (TryReadSensor(sensor) is RawSensor raw)
            {
                sensors.Add(raw);
            }
        }

        var children = new List<HardwareNode>();
        foreach (IHardware child in hardware.SubHardware)
        {
            children.Add(ReadNode(child));
        }

        return new HardwareNode(
            hardware.Identifier.ToString(),
            hardware.Name,
            MapRole(hardware.HardwareType),
            sensors,
            children);
    }

    private static RawSensor? TryReadSensor(ISensor sensor)
    {
        if (MapType(sensor.SensorType) is not RawSensorType type)
        {
            return null;
        }

        return new RawSensor(
            sensor.Identifier.ToString(),
            sensor.Name,
            type,
            sensor.Value,
            sensor.Index,
            sensor.Control is not null);
    }

    private static HardwareRole MapRole(HardwareType type) =>
        type switch
        {
            HardwareType.Cpu => HardwareRole.Cpu,
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => HardwareRole.Gpu,
            HardwareType.Motherboard => HardwareRole.Motherboard,
            HardwareType.SuperIO => HardwareRole.SuperIo,
            HardwareType.Cooler => HardwareRole.Controller,
            _ => HardwareRole.Other,
        };

    private static RawSensorType? MapType(SensorType type) =>
        type switch
        {
            SensorType.Temperature => RawSensorType.Temperature,
            SensorType.Fan => RawSensorType.Fan,
            SensorType.Control => RawSensorType.Control,
            SensorType.Power => RawSensorType.Power,
            SensorType.Flow => RawSensorType.Flow,
            SensorType.Clock => RawSensorType.Clock,
            SensorType.Load => RawSensorType.Load,
            _ => null,
        };
}
