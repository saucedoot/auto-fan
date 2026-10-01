namespace AutoFan.Core;

/// <summary>
/// Decides the next fan duties from a curve. It does not take control of a
/// draft, and it does not treat a power rise as a reason to spin faster.
/// Step sizes and the downward deadband are provisional.
/// </summary>
public static class CurveActuator
{
    public const int UpStepPercent = 15;

    public const int DownStepPercent = 5;

    public const double UpDeadbandCelsius = 0;

    public const double DownDeadbandCelsius = 0.5;

    public const int MeaningfulChangePercent = 5;

    public const string DraftStaysOff =
        "This curve is not validated. Fans stay on BIOS and the NVIDIA driver.";

    public static bool MayTakeControl(CoolingProfile? profile) =>
        profile is { State: ProfileState.Validated, Points.Count: > 0 };

    public static ActuatorDecision Next(
        CoolingProfile profile,
        ActuatorMemory memory,
        HardwareSnapshot snapshot,
        bool gpuFault,
        IReadOnlyDictionary<string, FanRunLimit>? limits = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (gpuFault)
        {
            return RestoreNow("The GPU reset or the graphics driver stopped.");
        }

        if (profile.Points.Count == 0)
        {
            return RestoreNow("There is no curve to run.");
        }

        double? cpu = PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature);
        double? gpu = PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature);
        var commands = new List<ActuatorCommand>();
        foreach (string groupId in profile.Points.Select(point => point.GroupId).Distinct(StringComparer.Ordinal))
        {
            CurvePoint[] cpuPoints = Points(profile, groupId, CurveSensor.Cpu);
            CurvePoint[] gpuPoints = Points(profile, groupId, CurveSensor.Gpu);
            if (cpuPoints.Length > 0 && cpu is null)
            {
                return RestoreNow("CPU temperature disappeared.");
            }

            if (gpuPoints.Length > 0 && gpu is null)
            {
                return RestoreNow("GPU temperature disappeared.");
            }

            int? cpuRequest = cpu is double cpuValue ? Lookup(cpuPoints, cpuValue) : null;
            int? gpuRequest = gpu is double gpuValue ? Lookup(gpuPoints, gpuValue) : null;
            if (cpuRequest is null && gpuRequest is null)
            {
                return RestoreNow("A needed temperature was missing.");
            }

            int requested = Math.Max(cpuRequest ?? 0, gpuRequest ?? 0);
            FanRunLimit limit = limits is not null && limits.TryGetValue(groupId, out FanRunLimit? found) && found is not null
                ? found
                : new FanRunLimit(null, null);
            double? rpm = Rpm(snapshot, groupId);
            int observed = ObservedDuty(snapshot, groupId) ?? requested;
            if (rpm is not > 0 && memory.AlreadySpunUp(groupId))
            {
                return Invalid("A fan did not spin after one spin-up.");
            }

            if (memory.MovedMeaningfully(groupId) && rpm is not > 0)
            {
                int spin = limit.StartDuty ?? 100;
                memory.MarkSpunUp(groupId);
                memory.Remember(groupId, spin, cpu, gpu, observed);
                commands.Add(new ActuatorCommand(groupId, spin));
                continue;
            }

            int duty = requested;
            if (memory.TryDuty(groupId, out int last))
            {
                if (!memory.TemperatureFell(groupId, cpu, gpu) && memory.HasTemperature(groupId) && requested < last)
                {
                    duty = last;
                }
                else
                {
                    int rise = Math.Min(UpStepPercent, Math.Max(0, requested - last));
                    int drop = Math.Min(DownStepPercent, Math.Max(0, last - requested));
                    duty = requested >= last ? last + rise : last - drop;
                }
            }
            else
            {
                int rise = Math.Min(UpStepPercent, Math.Max(0, requested - observed));
                int drop = Math.Min(DownStepPercent, Math.Max(0, observed - requested));
                duty = requested >= observed ? observed + rise : observed - drop;
            }

            if (rpm is not > 0 && limit.StartDuty is int start)
            {
                duty = Math.Max(duty, start);
            }

            if (limit.MinimumStableDuty is int minimum && rpm is > 0)
            {
                duty = Math.Max(duty, minimum);
            }

            memory.Remember(groupId, duty, cpu, gpu, observed);
            commands.Add(new ActuatorCommand(groupId, duty));
        }

        return new ActuatorDecision(false, false, null, commands);
    }

    public static ActuatorDecision RestoreNow(string reason) =>
        new(true, false, reason, []);

    private static ActuatorDecision Invalid(string reason) =>
        new(true, true, reason, []);

    private static CurvePoint[] Points(CoolingProfile profile, string groupId, CurveSensor sensor) =>
        profile.Points
            .Where(point => point.Sensor == sensor && string.Equals(point.GroupId, groupId, StringComparison.Ordinal))
            .OrderBy(point => point.TemperatureCelsius)
            .ToArray();

    private static int? Lookup(IReadOnlyList<CurvePoint> points, double temperature)
    {
        if (points.Count == 0)
        {
            return null;
        }

        if (temperature <= points[0].TemperatureCelsius)
        {
            return points[0].DutyPercent;
        }

        if (temperature >= points[^1].TemperatureCelsius)
        {
            return points[^1].DutyPercent;
        }

        for (int index = 1; index < points.Count; index++)
        {
            CurvePoint right = points[index];
            if (temperature > right.TemperatureCelsius)
            {
                continue;
            }

            CurvePoint left = points[index - 1];
            double span = right.TemperatureCelsius - left.TemperatureCelsius;
            if (span <= 0)
            {
                return Math.Max(left.DutyPercent, right.DutyPercent);
            }

            double fraction = (temperature - left.TemperatureCelsius) / span;
            return (int)Math.Round(left.DutyPercent + ((right.DutyPercent - left.DutyPercent) * fraction));
        }

        return points[^1].DutyPercent;
    }

    private static int? ObservedDuty(HardwareSnapshot snapshot, string groupId)
    {
        foreach (FanGroup group in snapshot.FanGroups)
        {
            if (string.Equals(group.Id, groupId, StringComparison.Ordinal))
            {
                return group.DutyCyclePercent;
            }
        }

        return null;
    }

    private static double? Rpm(HardwareSnapshot snapshot, string groupId)
    {
        foreach (FanGroup group in snapshot.FanGroups)
        {
            if (string.Equals(group.Id, groupId, StringComparison.Ordinal))
            {
                return group.Rpm;
            }
        }

        return null;
    }
}
