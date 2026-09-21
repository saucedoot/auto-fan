namespace AutoFan.Core;

public static class InteractionBuilder
{
    public static IReadOnlyList<InteractionEntry> Build(IReadOnlyList<InteractionSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var entries = new List<InteractionEntry>();
        foreach ((string firstId, string secondId) in DistinctPairs(samples))
        {
            IReadOnlyList<InteractionSample> pair = samples
                .Where(sample => sample.FirstGroupId == firstId && sample.SecondGroupId == secondId)
                .ToArray();
            if (pair.Count == 0)
            {
                continue;
            }

            InteractionSample named = pair[0];
            foreach (InfluenceTarget target in Enum.GetValues<InfluenceTarget>())
            {
                entries.Add(BuildEntry(
                    named.FirstGroupId,
                    named.FirstGroupName,
                    named.SecondGroupId,
                    named.SecondGroupName,
                    target,
                    pair));
            }
        }

        return entries;
    }

    public static string? InferNote(double? residualCelsius, InfluenceTarget target)
    {
        if (residualCelsius is not double residual
            || Math.Abs(residual) < InfluenceMapBuilder.NoneBandCelsius)
        {
            return null;
        }

        string name = TargetName(target);
        if (residual > 0)
        {
            return $"These fans cooled the {name} more together than alone. They may share an airflow path. That is a guess, not a measured airflow or pressure reading.";
        }

        return $"These fans cooled the {name} less together than the two alone added up. They may compete. That is a guess, not a measured airflow or pressure reading.";
    }

    public static string TargetName(InfluenceTarget target) =>
        target switch
        {
            InfluenceTarget.Cpu => "CPU",
            InfluenceTarget.Gpu => "GPU",
            InfluenceTarget.Vrm => "VRM",
            InfluenceTarget.Case => "Case",
            _ => target.ToString(),
        };

    private static InteractionEntry BuildEntry(
        string firstId,
        string firstName,
        string secondId,
        string secondName,
        InfluenceTarget target,
        IReadOnlyList<InteractionSample> pair)
    {
        double? first = Delta(pair, InteractionStep.ReferenceFirst, InteractionStep.First, target);
        double? second = Delta(pair, InteractionStep.ReferenceSecond, InteractionStep.Second, target);
        double? combined = Delta(pair, InteractionStep.ReferenceCombined, InteractionStep.Combined, target);
        if (first is not double firstDelta || second is not double secondDelta || combined is not double combinedDelta)
        {
            return new InteractionEntry(
                firstId,
                firstName,
                secondId,
                secondName,
                target,
                first,
                second,
                combined,
                ResidualCelsius: null,
                MetricEvidence.Unknown,
                InferredNote: null);
        }

        double residual = combinedDelta - (firstDelta + secondDelta);
        return new InteractionEntry(
            firstId,
            firstName,
            secondId,
            secondName,
            target,
            firstDelta,
            secondDelta,
            combinedDelta,
            residual,
            MetricEvidence.Measured,
            InferNote(residual, target));
    }

    private static double? Delta(
        IReadOnlyList<InteractionSample> pair,
        InteractionStep reference,
        InteractionStep perturb,
        InfluenceTarget target)
    {
        IReadOnlyList<double> before = Values(pair, reference, target);
        IReadOnlyList<double> after = Tail(Values(pair, perturb, target), ThermalDynamics.SettleWindowSamples);
        if (before.Count == 0 || after.Count == 0)
        {
            return null;
        }

        return before.Average() - after.Average();
    }

    private static IReadOnlyList<double> Values(
        IReadOnlyList<InteractionSample> pair,
        InteractionStep step,
        InfluenceTarget target)
    {
        var values = new List<double>();
        foreach (InteractionSample sample in pair)
        {
            if (sample.Step != step || !sample.Settled)
            {
                continue;
            }

            if (PreferredTemperature.Read(sample.Snapshot, target) is double value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static IReadOnlyList<double> Tail(IReadOnlyList<double> values, int count)
    {
        if (values.Count <= count)
        {
            return values;
        }

        return values.Skip(values.Count - count).ToArray();
    }

    private static IReadOnlyList<(string First, string Second)> DistinctPairs(IReadOnlyList<InteractionSample> samples)
    {
        var pairs = new List<(string First, string Second)>();
        foreach (InteractionSample sample in samples)
        {
            var key = (sample.FirstGroupId, sample.SecondGroupId);
            if (!pairs.Contains(key))
            {
                pairs.Add(key);
            }
        }

        return pairs;
    }
}
