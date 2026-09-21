using AutoFan.Core;

namespace AutoFan.Hardware;

public enum HardwareRole
{
    Cpu,
    Gpu,
    Motherboard,
    SuperIo,
    Controller,
    Other,
}

public enum RawSensorType
{
    Temperature,
    Fan,
    Control,
    Power,
    Flow,
    Clock,
    Load,
}

public sealed record RawSensor(
    string Id,
    string Name,
    RawSensorType Type,
    double? Value,
    int Index,
    bool HasSoftwareControl);

public sealed record HardwareNode(
    string Id,
    string Name,
    HardwareRole Role,
    IReadOnlyList<RawSensor> Sensors,
    IReadOnlyList<HardwareNode> Children);

public sealed record SensorTree(IReadOnlyList<HardwareNode> Hardware);

public sealed record MappedHardware(
    HardwareSnapshot Snapshot,
    HardwareIdentity Identity,
    IReadOnlyList<GpuDevice>? Gpus = null)
{
    public IReadOnlyList<GpuDevice> AvailableGpus => Gpus ?? [];
}
