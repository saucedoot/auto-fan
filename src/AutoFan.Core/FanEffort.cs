namespace AutoFan.Core;

/// <summary>
/// Whole-machine fan effort from observed RPM. The 0.05 band is provisional.
/// RPM divided by maximum RPM is not loudness.
/// </summary>
public static class FanEffort
{
    public const double ProvisionalBand = 0.05;

    public readonly record struct Score(double? Highest, double? Total, bool Complete);

    public static double? Ratio(double? rpm, double? maximumRpm)
    {
        if (rpm is not > 0 || maximumRpm is not > 0)
        {
            return null;
        }

        return Math.Clamp(rpm.Value / maximumRpm.Value, 0, 1);
    }

    public static Score Of(HardwareSnapshot snapshot, IReadOnlyDictionary<string, double> maximumRpm)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(maximumRpm);
        double highest = 0;
        double total = 0;
        int counted = 0;
        bool complete = true;
        foreach (FanGroup group in snapshot.FanGroups)
        {
            if (group.Rpm is not > 0)
            {
                continue;
            }

            double? ratio = maximumRpm.TryGetValue(group.Id, out double max) ? Ratio(group.Rpm, max) : null;
            if (ratio is not double value)
            {
                complete = false;
                continue;
            }

            counted++;
            highest = Math.Max(highest, value);
            total += value;
        }

        return counted == 0
            ? new Score(null, null, false)
            : new Score(highest, total, complete);
    }

    public static bool LikelyQuieter(
        HardwareSnapshot before,
        HardwareSnapshot after,
        IReadOnlyDictionary<string, double> maximumRpm)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(maximumRpm);
        bool slower = false;
        foreach (string id in Ids(before).Union(Ids(after), StringComparer.Ordinal))
        {
            FanGroup? left = Find(before, id);
            FanGroup? right = Find(after, id);
            if (left?.Rpm is not > 0 || right?.Rpm is not > 0 || !maximumRpm.TryGetValue(id, out double max))
            {
                return false;
            }

            double? beforeRatio = Ratio(left.Rpm, max);
            double? afterRatio = Ratio(right.Rpm, max);
            if (beforeRatio is not double from || afterRatio is not double to)
            {
                return false;
            }

            double change = to - from;
            bool pump = (left.Kind == FanGroupKind.Pump) || (right.Kind == FanGroupKind.Pump);
            if (pump && Math.Abs(change) <= ProvisionalBand)
            {
                continue;
            }

            if (change > ProvisionalBand)
            {
                return false;
            }

            if (change < -ProvisionalBand)
            {
                slower = true;
            }
        }

        return slower;
    }

    private static IEnumerable<string> Ids(HardwareSnapshot snapshot) =>
        snapshot.FanGroups.Where(group => group.Rpm is > 0).Select(group => group.Id);

    private static FanGroup? Find(HardwareSnapshot snapshot, string id) =>
        snapshot.FanGroups.FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.Ordinal));
}
