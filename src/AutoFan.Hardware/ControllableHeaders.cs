using LibreHardwareMonitor.Hardware;

namespace AutoFan.Hardware;

internal static class ControllableHeaders
{
    public static bool IsWritableHardware(HardwareType type) =>
        type is HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.Cooler;

    public static bool IsGpuHardware(HardwareType type) =>
        type is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    public static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> hardware)
    {
        foreach (IHardware node in hardware)
        {
            yield return node;
            foreach (IHardware child in Flatten(node.SubHardware))
            {
                yield return child;
            }
        }
    }

    public static IEnumerable<IControl> WritableControls(IComputer computer)
    {
        ArgumentNullException.ThrowIfNull(computer);

        foreach (IHardware node in Flatten(computer.Hardware))
        {
            if (!IsWritableHardware(node.HardwareType))
            {
                continue;
            }

            foreach (ISensor sensor in node.Sensors)
            {
                if (sensor.Control is { } control)
                {
                    yield return control;
                }
            }
        }
    }

    public static ControlLookup FindControl(IComputer computer, string fanGroupId)
    {
        ArgumentNullException.ThrowIfNull(computer);
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);

        foreach (IHardware node in Flatten(computer.Hardware))
        {
            foreach (ISensor sensor in node.Sensors)
            {
                if (!string.Equals(sensor.Identifier.ToString(), fanGroupId, StringComparison.Ordinal))
                {
                    continue;
                }

                IControl? control = sensor.Control ?? FindSiblingControl(node, sensor.Index);
                return new ControlLookup(node.HardwareType, control);
            }
        }

        return ControlLookup.NotFound;
    }

    private static IControl? FindSiblingControl(IHardware node, int index)
    {
        foreach (ISensor sensor in node.Sensors)
        {
            if (sensor.Index == index && sensor.Control is { } control)
            {
                return control;
            }
        }

        return null;
    }
}

internal readonly record struct ControlLookup(HardwareType? HardwareType, IControl? Control)
{
    public static ControlLookup NotFound { get; } = new(null, null);

    public bool Found => HardwareType is not null;
}
