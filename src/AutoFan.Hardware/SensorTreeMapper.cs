using AutoFan.Core;

namespace AutoFan.Hardware;

public static class SensorTreeMapper
{
    public static MappedHardware Map(
        SensorTree tree,
        DateTimeOffset? capturedAt = null,
        string? preferredGpuId = null)
    {
        ArgumentNullException.ThrowIfNull(tree);

        IReadOnlyList<GpuDevice> gpus = ListGpus(tree.Hardware);
        GpuDevice? selectedGpu = ResolveGpu(gpus, preferredGpuId);
        var sensors = new List<SensorReading>();
        var groups = new List<FanGroup>();
        foreach (HardwareNode node in Flatten(tree.Hardware))
        {
            if (ShouldSkipGpu(node, selectedGpu))
            {
                continue;
            }

            MapNode(node, sensors, groups);
        }

        HardwareIdentity identity = MapIdentity(tree.Hardware, selectedGpu);
        var snapshot = new HardwareSnapshot(
            capturedAt ?? DateTimeOffset.UtcNow,
            sensors,
            groups,
            IsDemoHardware: false);
        return new MappedHardware(snapshot, identity, gpus);
    }

    private static IEnumerable<HardwareNode> Flatten(IEnumerable<HardwareNode> nodes)
    {
        foreach (HardwareNode node in nodes)
        {
            yield return node;
            foreach (HardwareNode child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static void MapNode(
        HardwareNode node,
        List<SensorReading> sensors,
        List<FanGroup> groups)
    {
        foreach (RawSensor sensor in node.Sensors)
        {
            switch (sensor.Type)
            {
                case RawSensorType.Temperature:
                    sensors.Add(new SensorReading(
                        sensor.Id,
                        sensor.Name,
                        ClassifyTemperature(node.Role, sensor.Name),
                        sensor.Value,
                        "°C"));
                    break;
                case RawSensorType.Power:
                    if (ClassifyPower(node.Role) is SensorKind powerKind)
                    {
                        sensors.Add(new SensorReading(
                            sensor.Id,
                            sensor.Name,
                            powerKind,
                            sensor.Value,
                            "W"));
                    }

                    break;
                case RawSensorType.Clock:
                    if (ClassifyClock(node.Role) is SensorKind clockKind)
                    {
                        sensors.Add(new SensorReading(
                            sensor.Id,
                            sensor.Name,
                            clockKind,
                            sensor.Value,
                            "MHz"));
                    }

                    break;
                case RawSensorType.Load:
                    if (ClassifyLoad(node.Role) is SensorKind loadKind)
                    {
                        sensors.Add(new SensorReading(
                            sensor.Id,
                            sensor.Name,
                            loadKind,
                            sensor.Value,
                            "%"));
                    }

                    break;
            }
        }

        foreach (FanGroup group in MapFanGroups(node))
        {
            groups.Add(group);
            if (group.Rpm is double rpm)
            {
                SensorKind rpmKind = group.Kind == FanGroupKind.Pump
                    ? SensorKind.PumpRpm
                    : SensorKind.FanRpm;
                sensors.Add(new SensorReading(
                    $"{group.Id}-rpm",
                    $"{group.Name} RPM",
                    rpmKind,
                    rpm,
                    "RPM"));
            }

            if (group.DutyCyclePercent is int duty)
            {
                sensors.Add(new SensorReading(
                    $"{group.Id}-duty",
                    $"{group.Name} duty",
                    SensorKind.DutyPercent,
                    duty,
                    "%"));
            }
        }
    }

    private static List<FanGroup> MapFanGroups(HardwareNode node)
    {
        List<RawSensor> fans = node.Sensors.Where(static s => s.Type == RawSensorType.Fan).ToList();
        List<RawSensor> flows = node.Sensors.Where(static s => s.Type == RawSensorType.Flow).ToList();
        List<RawSensor> controls = node.Sensors.Where(static s => s.Type == RawSensorType.Control).ToList();
        IEnumerable<int> indexes = fans.Select(static s => s.Index)
            .Concat(flows.Select(static s => s.Index))
            .Concat(controls.Select(static s => s.Index))
            .Distinct()
            .OrderBy(static index => index);

        var groups = new List<FanGroup>();
        foreach (int index in indexes)
        {
            RawSensor? fan = fans.FirstOrDefault(s => s.Index == index);
            RawSensor? flow = flows.FirstOrDefault(s => s.Index == index);
            RawSensor? control = controls.FirstOrDefault(s => s.Index == index);
            string name = fan?.Name ?? flow?.Name ?? control?.Name ?? $"Group {index}";
            bool isPump = LooksLikePump(name) || (flow is not null && fan is null);
            bool controllable = control is not null
                || (fan?.HasSoftwareControl ?? false)
                || (flow?.HasSoftwareControl ?? false);
            string id = control?.Id ?? fan?.Id ?? flow?.Id ?? $"{node.Id}/group/{index}";
            int? duty = control?.Value is double value ? (int)Math.Round(value, MidpointRounding.AwayFromZero) : null;

            groups.Add(new FanGroup(
                id,
                name,
                duty,
                fan?.Value,
                ControllerName: node.Name,
                IsControllable: controllable,
                Kind: isPump ? FanGroupKind.Pump : FanGroupKind.Fan));
        }

        return groups;
    }

    internal static SensorKind ClassifyTemperature(HardwareRole role, string name)
    {
        if (role == HardwareRole.Cpu)
        {
            return IsCpuCoreSensor(name) ? SensorKind.OtherTemperature : SensorKind.CpuTemperature;
        }

        if (role == HardwareRole.Gpu)
        {
            if (Contains(name, "hot spot") || Contains(name, "hotspot")
                || Contains(name, "memory") || Contains(name, "junction"))
            {
                return SensorKind.OtherTemperature;
            }

            return SensorKind.GpuTemperature;
        }

        if (Contains(name, "ambient"))
        {
            return SensorKind.AmbientTemperature;
        }

        if (Contains(name, "vrm") || Contains(name, "mos"))
        {
            return SensorKind.VrmTemperature;
        }

        if (Contains(name, "case") || (Contains(name, "system") && !Contains(name, "agent")))
        {
            return SensorKind.CaseTemperature;
        }

        return SensorKind.MotherboardTemperature;
    }

    internal static SensorKind? ClassifyPower(HardwareRole role) =>
        role switch
        {
            HardwareRole.Cpu => SensorKind.CpuPower,
            HardwareRole.Gpu => SensorKind.GpuPower,
            _ => null,
        };

    internal static SensorKind? ClassifyClock(HardwareRole role) =>
        role switch
        {
            HardwareRole.Cpu => SensorKind.CpuClock,
            HardwareRole.Gpu => SensorKind.GpuClock,
            _ => null,
        };

    internal static SensorKind? ClassifyLoad(HardwareRole role) =>
        role switch
        {
            HardwareRole.Cpu => SensorKind.CpuLoad,
            HardwareRole.Gpu => SensorKind.GpuLoad,
            _ => null,
        };

    internal static IReadOnlyList<GpuDevice> ListGpus(IReadOnlyList<HardwareNode> hardware) =>
        Flatten(hardware)
            .Where(static node => node.Role == HardwareRole.Gpu && !string.IsNullOrWhiteSpace(node.Name))
            .Select(static node => new GpuDevice(node.Id, node.Name, GpuDeviceFilter.LooksDiscrete(node.Name)))
            .ToArray();

    internal static GpuDevice? ResolveGpu(IReadOnlyList<GpuDevice> gpus, string? preferredGpuId)
    {
        if (!string.IsNullOrWhiteSpace(preferredGpuId))
        {
            GpuDevice? preferred = gpus.FirstOrDefault(gpu =>
                string.Equals(gpu.Id, preferredGpuId, StringComparison.Ordinal));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return gpus.FirstOrDefault(static gpu => gpu.LooksDiscrete) ?? gpus.FirstOrDefault();
    }

    private static bool ShouldSkipGpu(HardwareNode node, GpuDevice? selectedGpu) =>
        node.Role == HardwareRole.Gpu
        && selectedGpu is not null
        && !string.Equals(node.Id, selectedGpu.Id, StringComparison.Ordinal);

    private static HardwareIdentity MapIdentity(IReadOnlyList<HardwareNode> hardware, GpuDevice? selectedGpu)
    {
        string? cpu = FirstName(hardware, HardwareRole.Cpu);
        string? gpu = selectedGpu?.Name ?? FirstName(hardware, HardwareRole.Gpu);
        string? motherboard = FirstName(hardware, HardwareRole.Motherboard);
        string displayName = string.IsNullOrWhiteSpace(motherboard) ? "This PC" : motherboard;
        return new HardwareIdentity(displayName, cpu, gpu, motherboard);
    }

    private static string? FirstName(IEnumerable<HardwareNode> nodes, HardwareRole role)
    {
        foreach (HardwareNode node in Flatten(nodes))
        {
            if (node.Role == role && !string.IsNullOrWhiteSpace(node.Name))
            {
                return node.Name;
            }
        }

        return null;
    }

    private static bool IsCpuCoreSensor(string name)
    {
        if (Contains(name, "ccd"))
        {
            return true;
        }

        return Contains(name, "core") && (name.Contains('#', StringComparison.Ordinal) || name.Any(char.IsDigit));
    }

    internal static bool LooksLikePump(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (IsNumberedFanName(name) || Contains(name, "pump fan") || Contains(name, "pump_fan"))
        {
            return false;
        }

        return Contains(name, "pump") || Contains(name, "aio");
    }

    private static bool IsNumberedFanName(string name)
    {
        string trimmed = name.Trim();
        return trimmed.StartsWith("fan", StringComparison.OrdinalIgnoreCase)
            && trimmed.Any(char.IsDigit);
    }

    private static bool Contains(string name, string token) =>
        name.Contains(token, StringComparison.OrdinalIgnoreCase);
}
