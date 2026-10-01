namespace AutoFan.Core;

public sealed record HomeCurvePointRow(
    string GroupId,
    CurveSensor Sensor,
    double TemperatureCelsius,
    int DutyPercent,
    string Kind,
    bool CanEdit)
{
    public string Label => Kind == "Unknown"
        ? "Unknown"
        : $"{Kind}: {TemperatureCelsius:0} °C → {DutyPercent}%";
}

public sealed record HomeCurveGroup(
    string GroupId,
    string DriverLabel,
    bool StaysOnBios,
    IReadOnlyList<HomeCurvePointRow> Points);

public sealed record HomeCurvePage(
    string StateLabel,
    string ControlLabel,
    IReadOnlyList<HomeCurveGroup> Groups);

/// <summary>
/// What Home shows for a saved curve. It does not command fans.
/// </summary>
public static class HomeCurveBoard
{
    public static HomeCurvePage Build(CoolingProfile? profile, IReadOnlyList<string>? knownGroupIds = null)
    {
        string state = profile is null
            ? "Not ready"
            : profile.State switch
            {
                ProfileState.Draft => "Draft",
                ProfileState.EditedCheckRequired => "Edited — check required",
                ProfileState.StaleCheckRequired => "Stale — check required",
                ProfileState.Invalid => "Invalid",
                ProfileState.Validated => "Validated",
                _ => profile.State.ToString(),
            };
        string control = profile is { State: ProfileState.Validated }
            ? "A validated curve may run. This PC is not validated yet."
            : "BIOS and the NVIDIA driver are still in control.";
        var groups = new List<HomeCurveGroup>();
        if (profile is not null)
        {
            foreach (string groupId in profile.Points.Select(point => point.GroupId).Distinct(StringComparer.Ordinal))
            {
                groups.Add(GroupFrom(profile, groupId));
            }
        }

        if (knownGroupIds is not null)
        {
            foreach (string groupId in knownGroupIds)
            {
                if (groups.Any(group => string.Equals(group.GroupId, groupId, StringComparison.Ordinal)))
                {
                    continue;
                }

                groups.Add(new HomeCurveGroup(
                    groupId,
                    "Stays on BIOS or the NVIDIA driver.",
                    true,
                    [new HomeCurvePointRow(groupId, CurveSensor.Cpu, 0, 0, "Unknown", false)]));
            }
        }

        return new HomeCurvePage(state, control, groups);
    }

    private static HomeCurveGroup GroupFrom(CoolingProfile profile, string groupId)
    {
        HomeCurvePointRow[] points = profile.Points
            .Where(point => string.Equals(point.GroupId, groupId, StringComparison.Ordinal))
            .OrderBy(point => point.Sensor)
            .ThenBy(point => point.TemperatureCelsius)
            .Select(point => new HomeCurvePointRow(
                point.GroupId,
                point.Sensor,
                point.TemperatureCelsius,
                point.DutyPercent,
                Kind(point),
                point.Origin != CurvePointOrigin.SafetyExtension))
            .ToArray();
        bool cpu = points.Any(point => point.Sensor == CurveSensor.Cpu && point.Kind != "Unknown");
        bool gpu = points.Any(point => point.Sensor == CurveSensor.Gpu && point.Kind != "Unknown");
        string driver = cpu && gpu
            ? "Uses the higher of the CPU and GPU requests."
            : gpu
                ? "Follows GPU temperature."
                : cpu
                    ? "Follows CPU temperature."
                    : "Stays on BIOS or the NVIDIA driver.";
        return new HomeCurveGroup(groupId, driver, driver.StartsWith("Stays", StringComparison.Ordinal), points);
    }

    private static string Kind(CurvePoint point) =>
        point.Origin switch
        {
            CurvePointOrigin.SafetyExtension => "Safety",
            CurvePointOrigin.SettledHold when point.Evidence == MetricEvidence.Measured => "Measured",
            CurvePointOrigin.MonotoneCorrection or CurvePointOrigin.DutyFloor => "Modeled",
            _ => "Unknown",
        };
}
