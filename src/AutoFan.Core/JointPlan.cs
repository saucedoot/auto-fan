namespace AutoFan.Core;

/// <summary>
/// Picks a few whole-fan settings from modeled screen rows. It does not
/// search every combination.
/// </summary>
public static class JointPlan
{
    public const int MaxVectors = 3;

    public static IReadOnlyList<JointVector> Select(
        HardwareSnapshot reference,
        IReadOnlyList<InfluenceEntry> influence,
        IReadOnlyList<FanActuation> actuation,
        ThermalAbortLimits limits)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(influence);
        ArgumentNullException.ThrowIfNull(actuation);

        var referenceDuty = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (FanGroup group in reference.FanGroups)
        {
            if (group.DutyCyclePercent is int found && found > 0)
            {
                referenceDuty[group.Id] = found;
            }
        }

        var best = new Dictionary<string, (int Duty, double Delta)>(StringComparer.Ordinal);
        foreach (InfluenceEntry entry in influence)
        {
            if (entry.Evidence != MetricEvidence.Modeled
                || entry.DeltaCelsius is not double delta
                || entry.DutyAfter is not int duty
                || duty <= 0)
            {
                continue;
            }

            if (!best.TryGetValue(entry.FanGroupId, out (int Duty, double Delta) current) || delta < current.Delta)
            {
                best[entry.FanGroupId] = (duty, delta);
            }
        }

        var vectors = new List<JointVector>();
        Add(vectors, Commands(best.Select(pair => new DutyCommand(pair.Key, pair.Value.Duty))), reference, influence, limits);
        if (best.Count > 0)
        {
            KeyValuePair<string, (int Duty, double Delta)> single = best.MinBy(pair => pair.Value.Delta);
            Add(vectors, [new DutyCommand(single.Key, single.Value.Duty)], reference, influence, limits);
        }

        var quiet = new List<DutyCommand>();
        foreach (FanActuation fan in actuation)
        {
            if (fan.MinimumStableDutyPercent is not int low || low <= 0)
            {
                continue;
            }

            if (referenceDuty.TryGetValue(fan.FanGroupId, out int current) && low < current)
            {
                quiet.Add(new DutyCommand(fan.FanGroupId, low));
            }
        }

        Add(vectors, quiet, reference, influence, limits);
        return vectors.Take(MaxVectors).ToArray();
    }

    public static IReadOnlyList<DutyCommand>? OnePair(
        IReadOnlyList<DutyCommand> vector,
        IReadOnlyList<InfluenceEntry> influence,
        bool residualTooLarge,
        bool pairAlreadyUsed)
    {
        if (!residualTooLarge || pairAlreadyUsed || vector.Count < 3)
        {
            return null;
        }

        DutyCommand[] pair = vector
            .OrderBy(command => ModeledDelta(influence, command))
            .Take(2)
            .ToArray();
        return pair.Length == 2 ? pair : null;
    }

    private static void Add(
        List<JointVector> vectors,
        IReadOnlyList<DutyCommand> commands,
        HardwareSnapshot reference,
        IReadOnlyList<InfluenceEntry> influence,
        ThermalAbortLimits limits)
    {
        if (commands.Count == 0 || vectors.Count >= MaxVectors)
        {
            return;
        }

        if (vectors.Any(vector => Same(vector.Commands, commands)))
        {
            return;
        }

        double? cpu = Predict(reference, influence, commands, SensorKind.CpuTemperature);
        double? gpu = Predict(reference, influence, commands, SensorKind.GpuTemperature);
        if (cpu is >= 0 && cpu >= limits.CpuCelsius)
        {
            return;
        }

        if (gpu is >= 0 && gpu >= limits.GpuCelsius)
        {
            return;
        }

        vectors.Add(new JointVector(commands.ToArray(), cpu, gpu));
    }

    private static double? Predict(
        HardwareSnapshot reference,
        IReadOnlyList<InfluenceEntry> influence,
        IReadOnlyList<DutyCommand> commands,
        SensorKind kind)
    {
        double? start = PreferredTemperature.Read(reference, kind);
        if (start is not double value)
        {
            return null;
        }

        InfluenceTarget target = kind == SensorKind.GpuTemperature ? InfluenceTarget.Gpu : InfluenceTarget.Cpu;
        foreach (DutyCommand command in commands)
        {
            InfluenceEntry? entry = influence.FirstOrDefault(item =>
                item.Evidence == MetricEvidence.Modeled
                && item.Target == target
                && string.Equals(item.FanGroupId, command.GroupId, StringComparison.Ordinal)
                && item.DutyAfter == command.DutyPercent
                && item.DeltaCelsius is not null);
            if (entry?.DeltaCelsius is not double delta)
            {
                return null;
            }

            value += delta;
        }

        return value;
    }

    private static double ModeledDelta(IReadOnlyList<InfluenceEntry> influence, DutyCommand command)
    {
        return influence
            .Where(item => item.Evidence == MetricEvidence.Modeled
                && string.Equals(item.FanGroupId, command.GroupId, StringComparison.Ordinal)
                && item.DutyAfter == command.DutyPercent
                && item.DeltaCelsius is not null)
            .Select(item => item.DeltaCelsius ?? 0)
            .DefaultIfEmpty(0)
            .Min();
    }

    private static IReadOnlyList<DutyCommand> Commands(IEnumerable<DutyCommand> commands) =>
        commands.OrderBy(command => command.GroupId, StringComparer.Ordinal).ToArray();

    private static bool Same(IReadOnlyList<DutyCommand> left, IReadOnlyList<DutyCommand> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (DutyCommand command in left)
        {
            if (!right.Any(item =>
                string.Equals(item.GroupId, command.GroupId, StringComparison.Ordinal)
                && item.DutyPercent == command.DutyPercent))
            {
                return false;
            }
        }

        return true;
    }
}
