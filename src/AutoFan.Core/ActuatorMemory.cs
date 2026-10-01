namespace AutoFan.Core;

public sealed record FanRunLimit(int? MinimumStableDuty, int? StartDuty);

public sealed record ActuatorCommand(string GroupId, int DutyPercent);

public sealed record ActuatorDecision(
    bool Restore,
    bool Invalid,
    string? Reason,
    IReadOnlyList<ActuatorCommand> Commands);

/// <summary>
/// Remembers the last command so the next tick can ramp, wait out a small
/// drop, and allow one spin-up. The step sizes are provisional.
/// </summary>
public sealed class ActuatorMemory
{
    private readonly Dictionary<string, int> _duty = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _cpu = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _gpu = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _spunUp = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _previousDuty = new(StringComparer.Ordinal);

    public bool TryDuty(string groupId, out int duty) => _duty.TryGetValue(groupId, out duty);

    public void Remember(string groupId, int duty, double? cpuCelsius, double? gpuCelsius, int? observedDuty)
    {
        if (_duty.TryGetValue(groupId, out int previous))
        {
            _previousDuty[groupId] = previous;
        }
        else if (observedDuty is int observed)
        {
            _previousDuty[groupId] = observed;
        }

        _duty[groupId] = duty;
        if (cpuCelsius is double cpu)
        {
            _cpu[groupId] = cpu;
        }

        if (gpuCelsius is double gpu)
        {
            _gpu[groupId] = gpu;
        }
    }

    public bool MovedMeaningfully(string groupId)
    {
        return _duty.TryGetValue(groupId, out int duty)
            && _previousDuty.TryGetValue(groupId, out int previous)
            && Math.Abs(duty - previous) >= CurveActuator.MeaningfulChangePercent;
    }

    public bool AlreadySpunUp(string groupId) => _spunUp.TryGetValue(groupId, out bool used) && used;

    public void MarkSpunUp(string groupId) => _spunUp[groupId] = true;

    public bool TemperatureFell(string groupId, double? cpuCelsius, double? gpuCelsius)
    {
        bool cpuFell = Fell(_cpu, groupId, cpuCelsius);
        bool gpuFell = Fell(_gpu, groupId, gpuCelsius);
        bool cpuRose = Rose(_cpu, groupId, cpuCelsius);
        bool gpuRose = Rose(_gpu, groupId, gpuCelsius);
        if (cpuRose || gpuRose)
        {
            return false;
        }

        return cpuFell || gpuFell;
    }

    public bool HasTemperature(string groupId) => _cpu.ContainsKey(groupId) || _gpu.ContainsKey(groupId);

    private static bool Fell(Dictionary<string, double> values, string groupId, double? current) =>
        current is double now
        && values.TryGetValue(groupId, out double previous)
        && previous - now > CurveActuator.DownDeadbandCelsius;

    private static bool Rose(Dictionary<string, double> values, string groupId, double? current) =>
        current is double now
        && values.TryGetValue(groupId, out double previous)
        && now - previous > CurveActuator.UpDeadbandCelsius;
}
