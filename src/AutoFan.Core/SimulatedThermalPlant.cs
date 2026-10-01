namespace AutoFan.Core;

/// <summary>
/// Tests-only PC. Time moves only when <see cref="Advance"/> is called.
/// It is not a source for the slope or power limits.
/// </summary>
public sealed class SimulatedThermalPlant : IHardwareBackend
{
    public const string CpuFanId = "sim-cpu-fan";

    public const string GpuFanId = "sim-gpu-fan";

    public const double TimeConstantSeconds = 8;

    private const double CoolingAtFullDutyCelsius = 15;

    private const double CrossCouple = 0.25;

    private const double CpuHeatCelsius = 75;

    private const double GpuHeatCelsius = 68;

    private const double FullRpm = 1500;

    private readonly List<PlantDutyCommand> _commands = [];

    private DateTimeOffset _now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private TimeSpan _elapsed;

    private int _step;

    private int _cpuDuty = 40;

    private int _gpuDuty = 40;

    private double _cpuTemp;

    private double _gpuTemp;

    private double _cpuPower = 80;

    private double _gpuPower = 120;

    private bool _software;

    public SimulatedThermalPlant()
    {
        RestAt(_cpuDuty, _gpuDuty);
    }

    public string DisplayName => "Simulated PC";

    public bool IsDemo => true;

    public bool HasActiveSoftwareControl => _software;

    public int BiosCpuDuty { get; set; } = 40;

    public int BiosGpuDuty { get; set; } = 40;

    public bool FightWrites { get; set; }

    public bool StallCpuFan { get; set; }

    public double NoiseCelsius { get; set; } = 0.02;

    public double CreepCelsiusPerSecond { get; set; }

    public double PowerDriftWattsPerSecond { get; set; }

    public double TemperatureDriftCelsiusPerSecond { get; set; }

    public TimeSpan? DropCpuTemperatureAfter { get; set; }

    public TimeSpan? OverheatCpuAfter { get; set; }

    public IReadOnlyList<PlantDutyCommand> Commands => _commands;

    public IReadOnlyList<FanGroup> FanGroups => BuildGroups();

    public void RestAt(int cpuDuty, int gpuDuty)
    {
        _cpuDuty = cpuDuty;
        _gpuDuty = gpuDuty;
        _cpuTemp = CpuEquilibrium(cpuDuty, gpuDuty);
        _gpuTemp = GpuEquilibrium(cpuDuty, gpuDuty);
    }

    public void Advance(TimeSpan step)
    {
        if (step < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        _elapsed += step;
        _step++;
        if (OverheatCpuAfter is TimeSpan overheat && _elapsed >= overheat)
        {
            _cpuTemp = SafetyLimits.CpuAbortCelsius + 2;
        }
        else
        {
            double blend = 1 - Math.Exp(-step.TotalSeconds / TimeConstantSeconds);
            double creep = CreepCelsiusPerSecond * _elapsed.TotalSeconds;
            _cpuTemp += (CpuEquilibrium(_cpuDuty, _gpuDuty) + creep - _cpuTemp) * blend;
            _gpuTemp += (GpuEquilibrium(_cpuDuty, _gpuDuty) - _gpuTemp) * blend;
            _cpuTemp += TemperatureDriftCelsiusPerSecond * step.TotalSeconds;
        }

        _cpuPower += PowerDriftWattsPerSecond * step.TotalSeconds;
        if (FightWrites)
        {
            _cpuDuty = BiosCpuDuty;
            _gpuDuty = BiosGpuDuty;
        }

        _now += step;
    }

    public HardwareSnapshot ReadSnapshot()
    {
        double noise = _step % 2 == 0 ? NoiseCelsius : -NoiseCelsius;
        bool hideCpu = DropCpuTemperatureAfter is TimeSpan drop && _elapsed >= drop;
        var sensors = new List<SensorReading>();
        if (!hideCpu)
        {
            sensors.Add(new SensorReading("cpu", "CPU Package", SensorKind.CpuTemperature, _cpuTemp + noise, "°C"));
        }

        sensors.Add(new SensorReading("gpu", "GPU", SensorKind.GpuTemperature, _gpuTemp + noise, "°C"));
        sensors.Add(new SensorReading("cpu-power", "CPU Package", SensorKind.CpuPower, _cpuPower, "W"));
        sensors.Add(new SensorReading("gpu-power", "GPU Package", SensorKind.GpuPower, _gpuPower, "W"));
        sensors.Add(new SensorReading("cpu-clock", "CPU Clock", SensorKind.CpuClock, 4000, "MHz"));
        sensors.Add(new SensorReading("gpu-clock", "GPU Clock", SensorKind.GpuClock, 1800, "MHz"));
        sensors.Add(new SensorReading("cpu-load", "CPU Load", SensorKind.CpuLoad, 25, "%"));
        sensors.Add(new SensorReading("gpu-load", "GPU Load", SensorKind.GpuLoad, 40, "%"));
        return new HardwareSnapshot(_now, sensors, BuildGroups(), IsDemoHardware: true);
    }

    public DutySetResult TrySetDuty(string fanGroupId, int percent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanGroupId);
        if (!DutyPercent.TryCreate(percent, out _))
        {
            return new DutySetResult(
                false,
                $"Duty cycle {percent}% is outside {SafetyLimits.MinDutyPercent}-{SafetyLimits.MaxDutyPercent}%.");
        }

        if (fanGroupId == CpuFanId)
        {
            _cpuDuty = percent;
        }
        else if (fanGroupId == GpuFanId)
        {
            _gpuDuty = percent;
        }
        else
        {
            return new DutySetResult(false, $"Unknown fan group '{fanGroupId}'.");
        }

        _software = true;
        _commands.Add(new PlantDutyCommand(fanGroupId, percent, SoftwareControl: true));
        return new DutySetResult(true, Error: null);
    }

    public void RestoreDefaults()
    {
        _cpuDuty = BiosCpuDuty;
        _gpuDuty = BiosGpuDuty;
        _software = false;
        _commands.Add(new PlantDutyCommand(CpuFanId, BiosCpuDuty, SoftwareControl: false));
        _commands.Add(new PlantDutyCommand(GpuFanId, BiosGpuDuty, SoftwareControl: false));
    }

    public double CpuEquilibrium(int cpuDuty, int gpuDuty) =>
        CpuHeatCelsius - Cooling(cpuDuty) - (CrossCouple * Cooling(gpuDuty));

    public double GpuEquilibrium(int cpuDuty, int gpuDuty) =>
        GpuHeatCelsius - Cooling(gpuDuty) - (CrossCouple * Cooling(cpuDuty));

    private static double Cooling(int duty) => duty / 100.0 * CoolingAtFullDutyCelsius;

    private List<FanGroup> BuildGroups() =>
    [
        new FanGroup(CpuFanId, "CPU fan", _cpuDuty, Rpm(_cpuDuty, StallCpuFan), "Simulated", IsControllable: true),
        new FanGroup(GpuFanId, "GPU fan", _gpuDuty, Rpm(_gpuDuty, stalled: false), "Simulated", IsControllable: true),
    ];

    private static double Rpm(int duty, bool stalled) => stalled ? 0 : duty / 100.0 * FullRpm;
}

public readonly record struct PlantDutyCommand(string FanGroupId, int Percent, bool SoftwareControl);
