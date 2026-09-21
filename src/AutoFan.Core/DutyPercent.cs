namespace AutoFan.Core;

/// <summary>
/// A fan duty cycle that is guaranteed to be inside
/// <see cref="SafetyLimits.MinDutyPercent"/>–<see cref="SafetyLimits.MaxDutyPercent"/>.
/// Values outside that range cannot be constructed.
/// </summary>
public readonly record struct DutyPercent
{
    public int Value { get; }

    private DutyPercent(int value)
    {
        Value = value;
    }

    public static bool TryCreate(int value, out DutyPercent duty)
    {
        if (!SafetyLimits.IsDutyInRange(value))
        {
            duty = default;
            return false;
        }

        duty = new DutyPercent(value);
        return true;
    }

    public static DutyPercent Create(int value)
    {
        if (!TryCreate(value, out DutyPercent duty))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Duty cycle must be between {SafetyLimits.MinDutyPercent} and {SafetyLimits.MaxDutyPercent} percent.");
        }

        return duty;
    }

    public override string ToString() => $"{Value}%";
}
