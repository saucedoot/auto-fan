namespace AutoFan.Core;

/// <summary>
/// Changes one draft duty. The safety point stays. The result needs a check
/// and is never validated.
/// </summary>
public static class CurveEdit
{
    public static bool TryApply(
        CoolingProfile profile,
        string groupId,
        CurveSensor sensor,
        double temperatureCelsius,
        int dutyPercent,
        out CoolingProfile updated,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(profile);
        updated = profile;
        if (dutyPercent is < 1 or > 100)
        {
            error = "Duty must stay between 1% and 100%.";
            return false;
        }

        int index = -1;
        for (int i = 0; i < profile.Points.Count; i++)
        {
            CurvePoint point = profile.Points[i];
            if (!string.Equals(point.GroupId, groupId, StringComparison.Ordinal)
                || point.Sensor != sensor
                || Math.Abs(point.TemperatureCelsius - temperatureCelsius) > 0.05)
            {
                continue;
            }

            index = i;
            break;
        }

        if (index < 0)
        {
            error = "That point is not on the curve.";
            return false;
        }

        if (profile.Points[index].Origin == CurvePointOrigin.SafetyExtension)
        {
            error = "The safety point stays.";
            return false;
        }

        var points = profile.Points.ToList();
        points[index] = points[index] with
        {
            DutyPercent = dutyPercent,
            Evidence = MetricEvidence.Modeled,
            Origin = CurvePointOrigin.MonotoneCorrection,
        };
        EnforceMonotone(points, groupId, sensor);
        updated = profile with
        {
            State = ProfileState.EditedCheckRequired,
            Detail = CurveBuilder.CheckDetail,
            Points = points,
        };
        error = null;
        return true;
    }

    private static void EnforceMonotone(List<CurvePoint> points, string groupId, CurveSensor sensor)
    {
        List<int> indexes = [];
        for (int i = 0; i < points.Count; i++)
        {
            if (string.Equals(points[i].GroupId, groupId, StringComparison.Ordinal) && points[i].Sensor == sensor)
            {
                indexes.Add(i);
            }
        }

        indexes.Sort((left, right) => points[left].TemperatureCelsius.CompareTo(points[right].TemperatureCelsius));
        for (int i = 1; i < indexes.Count; i++)
        {
            CurvePoint previous = points[indexes[i - 1]];
            CurvePoint current = points[indexes[i]];
            if (current.Origin == CurvePointOrigin.SafetyExtension)
            {
                continue;
            }

            if (current.DutyPercent >= previous.DutyPercent)
            {
                continue;
            }

            points[indexes[i]] = current with
            {
                DutyPercent = previous.DutyPercent,
                Evidence = MetricEvidence.Modeled,
                Origin = CurvePointOrigin.MonotoneCorrection,
            };
        }
    }
}
