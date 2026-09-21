using AutoFan.Core;

namespace AutoFan.Hardware;

/// <summary>
/// In-memory demo PC. Duty changes only mutate RAM-backed RPM. This backend
/// never opens drivers, Super I/O, or vendor GPU APIs.
/// </summary>
public sealed class FakeHardwareBackend : IHardwareBackend
{
    public const string CpuSensorId = "cpu-temp";
    public const string GpuSensorId = "gpu-temp";
    public const string VrmSensorId = "vrm-temp";
    public const string AmbientSensorId = "ambient-temp";
    public const string CpuPowerId = "cpu-power";
    public const string GpuPowerId = "gpu-power";
    public const string CpuClockId = "cpu-clock";
    public const string GpuClockId = "gpu-clock";
    public const string CpuLoadId = "cpu-load";
    public const string GpuLoadId = "gpu-load";
    public const string FrontFanId = "front-intake";
    public const string RearFanId = "rear-exhaust";
    public const string TopFanId = "top-exhaust";
    public const string PumpId = "aio-pump";
    public const string GpuFanId = "gpu-fan";
    public const string AmdGpuFanId = "amd-gpu-fan";

    public const string PumpRejectedMessage = "Pump duty cycles are not written.";
    public const string GpuRejectedMessage = FanTestReasons.Gpu;

    private readonly Dictionary<string, double> _temperatures;
    private readonly Dictionary<string, double?> _readings;
    private readonly Dictionary<string, FanState> _fans;
    private readonly Dictionary<string, int> _originalDuties;
    private bool _includeAmbient;

    public FakeHardwareBackend()
    {
        _temperatures = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [CpuSensorId] = 45,
            [GpuSensorId] = 42,
            [VrmSensorId] = 40,
        };
        _readings = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            [CpuPowerId] = 35,
            [GpuPowerId] = 20,
            [CpuClockId] = 3800,
            [GpuClockId] = 1500,
            [CpuLoadId] = 8,
            [GpuLoadId] = 5,
            [AmbientSensorId] = 24,
        };

        _fans = new Dictionary<string, FanState>(StringComparer.Ordinal)
        {
            [FrontFanId] = new FanState("Front intake", 35, 1400),
            [RearFanId] = new FanState("Rear exhaust", 30, 1200),
            [TopFanId] = new FanState("Top exhaust", 25, 1100),
            [PumpId] = new FanState("AIO pump", 50, 3000, FanGroupKind.Pump),
            [GpuFanId] = new FanState(
                "GPU Fan",
                40,
                2000,
                IsGpu: true,
                ControllerName: "NVIDIA GeForce RTX 4070"),
            [AmdGpuFanId] = new FanState(
                "GPU Fan",
                35,
                1800,
                IsGpu: true,
                ControllerName: "AMD Radeon RX 7800 XT"),
        };

        _originalDuties = _fans.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Duty,
            StringComparer.Ordinal);
    }

    public string DisplayName => "Demo PC (not this machine)";

    public bool IsDemo => true;

    public bool HasActiveSoftwareControl { get; private set; }

    public IReadOnlyList<FanGroup> FanGroups => BuildFanGroups();

    public void OverrideTemperature(string sensorId, double celsius)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorId);
        if (!_temperatures.ContainsKey(sensorId))
        {
            throw new ArgumentException($"Unknown sensor '{sensorId}'.", nameof(sensorId));
        }

        _temperatures[sensorId] = celsius;
    }

    public void IncludeAmbient(double? celsius = 24)
    {
        _includeAmbient = true;
        _readings[AmbientSensorId] = celsius;
    }

    public void OverrideReading(string sensorId, double? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorId);
        if (!_readings.ContainsKey(sensorId))
        {
            throw new ArgumentException($"Unknown reading '{sensorId}'.", nameof(sensorId));
        }

        _readings[sensorId] = value;
    }

    public HardwareSnapshot ReadSnapshot()
    {
        var sensors = new List<SensorReading>
        {
            new SensorReading(CpuSensorId, "CPU", SensorKind.CpuTemperature, _temperatures[CpuSensorId], "°C"),
            new SensorReading(GpuSensorId, "GPU", SensorKind.GpuTemperature, _temperatures[GpuSensorId], "°C"),
            new SensorReading(VrmSensorId, "VRM", SensorKind.VrmTemperature, _temperatures[VrmSensorId], "°C"),
            new SensorReading(CpuPowerId, "CPU Package", SensorKind.CpuPower, _readings[CpuPowerId], "W"),
            new SensorReading(GpuPowerId, "GPU Package", SensorKind.GpuPower, _readings[GpuPowerId], "W"),
            new SensorReading(CpuClockId, "CPU Clock", SensorKind.CpuClock, _readings[CpuClockId], "MHz"),
            new SensorReading(GpuClockId, "GPU Clock", SensorKind.GpuClock, _readings[GpuClockId], "MHz"),
            new SensorReading(CpuLoadId, "CPU Load", SensorKind.CpuLoad, _readings[CpuLoadId], "%"),
            new SensorReading(GpuLoadId, "GPU Load", SensorKind.GpuLoad, _readings[GpuLoadId], "%"),
        };

        if (_includeAmbient)
        {
            sensors.Add(new SensorReading(
                AmbientSensorId,
                "Ambient",
                SensorKind.AmbientTemperature,
                _readings[AmbientSensorId],
                "°C"));
        }

        foreach ((string id, FanState fan) in _fans)
        {
            double rpm = ComputeRpm(fan);
            SensorKind rpmKind = fan.Kind == FanGroupKind.Pump ? SensorKind.PumpRpm : SensorKind.FanRpm;
            sensors.Add(new SensorReading($"{id}-rpm", $"{fan.Name} RPM", rpmKind, rpm, "RPM"));
            sensors.Add(new SensorReading($"{id}-duty", $"{fan.Name} duty", SensorKind.DutyPercent, fan.Duty, "%"));
        }

        return new HardwareSnapshot(DateTimeOffset.UtcNow, sensors, BuildFanGroups(), IsDemoHardware: true);
    }

    public DutySetResult TrySetDuty(string fanGroupId, int percent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);

        if (!DutyPercent.TryCreate(percent, out DutyPercent duty))
        {
            return new DutySetResult(
                false,
                $"Duty cycle {percent}% is outside {SafetyLimits.MinDutyPercent}-{SafetyLimits.MaxDutyPercent}%.");
        }

        if (!_fans.TryGetValue(fanGroupId, out FanState? fan) || fan is null)
        {
            return new DutySetResult(false, $"Unknown fan group '{fanGroupId}'.");
        }

        if (fan.Kind == FanGroupKind.Pump)
        {
            return new DutySetResult(false, PumpRejectedMessage);
        }

        if (fan.IsGpu && FanWriteCandidates.LooksAmdOrIntelGpu(fan.ControllerName, fan.Name))
        {
            return new DutySetResult(false, GpuRejectedMessage);
        }

        ThermalAbortReason? abort = SafetyLimits.Evaluate(ReadSnapshot());
        if (abort is not null)
        {
            RestoreDefaults();
            return new DutySetResult(false, SafetyLimits.Describe(abort.Value));
        }

        _fans[fanGroupId] = fan with { Duty = duty.Value };
        ApplyCoupledNvidiaDuty(fanGroupId, duty.Value);
        HasActiveSoftwareControl = true;
        return new DutySetResult(true, Error: null);
    }

    public void RestoreDefaults()
    {
        foreach (string id in _fans.Keys.ToArray())
        {
            FanState fan = _fans[id];
            _fans[id] = fan with { Duty = _originalDuties[id] };
        }

        HasActiveSoftwareControl = false;
    }

    private void ApplyCoupledNvidiaDuty(string writtenId, int percent)
    {
        FanGroup? written = BuildFanGroups().FirstOrDefault(group =>
            string.Equals(group.Id, writtenId, StringComparison.Ordinal));
        string? key = written is null ? null : FanWriteCandidates.CoupledSetKey(written);
        if (key is null)
        {
            return;
        }

        foreach (FanGroup sibling in BuildFanGroups())
        {
            if (string.Equals(sibling.Id, writtenId, StringComparison.Ordinal)
                || !string.Equals(FanWriteCandidates.CoupledSetKey(sibling), key, StringComparison.Ordinal))
            {
                continue;
            }

            _fans[sibling.Id] = _fans[sibling.Id] with { Duty = percent };
        }
    }

    private List<FanGroup> BuildFanGroups()
    {
        var groups = new List<FanGroup>(_fans.Count);
        foreach ((string id, FanState fan) in _fans)
        {
            groups.Add(new FanGroup(
                id,
                fan.Name,
                fan.Duty,
                ComputeRpm(fan),
                ControllerName: fan.ControllerName
                    ?? (fan.IsGpu ? "Demo GPU" : "Demo controller"),
                IsControllable: true,
                Kind: fan.Kind));
        }

        return groups;
    }

    private static double ComputeRpm(FanState fan) =>
        fan.RespondsToDuty ? fan.MaxRpm * fan.Duty / 100.0 : 0;

    private sealed record FanState(
        string Name,
        int Duty,
        double MaxRpm,
        FanGroupKind Kind = FanGroupKind.Fan,
        bool IsGpu = false,
        bool RespondsToDuty = true,
        string? ControllerName = null);

    public void AddFan(
        string id,
        string name,
        int duty,
        double maxRpm,
        bool respondsToDuty = true,
        FanGroupKind kind = FanGroupKind.Fan,
        bool isGpu = false,
        string? controllerName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _fans[id] = new FanState(name, duty, maxRpm, kind, isGpu, respondsToDuty, controllerName);
        _originalDuties[id] = duty;
    }
}
