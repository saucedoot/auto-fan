namespace AutoFan.Core;

public sealed record FanTopology(
    string GroupId,
    string Controller,
    string? CoupledSetKey,
    int? MinimumStableDutyPercent,
    int? StartDutyPercent,
    double? MaximumRpm);

/// <summary>
/// Identity of this PC's cooling layout. A profile may be reused only when
/// the GPU hardware id and the fan group ids still match. The display name
/// is not that id.
/// </summary>
public sealed record TopologyFingerprint(
    string? MotherboardName,
    string? CpuName,
    string? GpuHardwareId,
    string? GpuName,
    IReadOnlyList<string> SensorIds,
    IReadOnlyList<FanTopology> Fans)
{
    public bool Matches(TopologyFingerprint other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (string.IsNullOrWhiteSpace(GpuHardwareId)
            || string.IsNullOrWhiteSpace(other.GpuHardwareId)
            || !string.Equals(GpuHardwareId, other.GpuHardwareId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!Same(MotherboardName, other.MotherboardName) || !Same(CpuName, other.CpuName))
        {
            return false;
        }

        string[] mine = Fans.Select(fan => fan.GroupId).Order(StringComparer.Ordinal).ToArray();
        string[] theirs = other.Fans.Select(fan => fan.GroupId).Order(StringComparer.Ordinal).ToArray();
        return mine.SequenceEqual(theirs, StringComparer.Ordinal);
    }

    public static TopologyFingerprint From(
        HardwareIdentity identity,
        HardwareSnapshot snapshot,
        IReadOnlyList<FanActuation> actuation)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(actuation);

        var sensors = snapshot.Sensors
            .Where(sensor => sensor.Kind is SensorKind.CpuTemperature
                or SensorKind.GpuTemperature
                or SensorKind.CpuPower
                or SensorKind.GpuPower)
            .Select(sensor => sensor.Id)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var fans = new List<FanTopology>();
        foreach (FanGroup group in snapshot.FanGroups)
        {
            if (group.Kind == FanGroupKind.Pump || !group.IsControllable)
            {
                continue;
            }

            FanActuation? measured = actuation.FirstOrDefault(item =>
                string.Equals(item.FanGroupId, group.Id, StringComparison.Ordinal));
            fans.Add(new FanTopology(
                group.Id,
                group.ControllerName,
                FanWriteCandidates.CoupledSetKey(group),
                measured?.MinimumStableDutyPercent,
                measured?.StartDutyPercent,
                measured?.MaximumRpm));
        }

        return new TopologyFingerprint(
            identity.MotherboardName,
            identity.CpuName,
            identity.GpuHardwareId,
            identity.GpuName,
            sensors,
            fans);
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
}
