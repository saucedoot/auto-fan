namespace AutoFan.Core;

/// <summary>
/// Remembers pumps that were spinning. A later sample with no RPM, during
/// synthetic heat, aborts the run. Pumps are never written.
/// </summary>
public sealed class PumpWatch
{
    public const string SilentDetail =
        "A pump that was spinning stopped reporting speed. Stopping and restoring fans.";

    private readonly HashSet<string> _spinning;

    private PumpWatch(IEnumerable<string> spinningIds)
    {
        _spinning = new HashSet<string>(spinningIds, StringComparer.Ordinal);
    }

    public static PumpWatch Capture(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IEnumerable<string> spinning = snapshot.FanGroups
            .Where(group => group.Kind == FanGroupKind.Pump && group.Rpm is > 0)
            .Select(group => group.Id);
        return new PumpWatch(spinning);
    }

    public string? Check(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (string id in _spinning)
        {
            FanGroup? group = snapshot.FanGroups.FirstOrDefault(fan =>
                string.Equals(fan.Id, id, StringComparison.Ordinal));
            if (group is null || group.Rpm is not > 0)
            {
                return SilentDetail;
            }
        }

        return null;
    }
}
