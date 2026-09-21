namespace AutoFan.Core;

public static class ExplanationReportBuilder
{
    public const string WanderHeading = "How steady this PC was";
    public const string PrimaryGpuPathHeading = "Primary GPU cooling path";
    public const string PrimaryCpuPathHeading = "Primary CPU cooling path";
    public const string MostEffectiveFanHeading = "Most effective fan";
    public const string DiminishingReturnsHeading = "Largest source of diminishing returns";
    public const string DetectedInteractionHeading = "Detected interaction";
    public const string RecommendedPointHeading = "Recommended operating point";
    public const string ExpectedResultHeading = "Expected result";
    public const string ConfirmationHeading = "Confirmation";

    public const string NeedFanTests = PolicyOptimizer.NeedFanTestsReason;
    public const string NeedSpeedCurve = "Need a measured speed-versus-temperature curve.";
    public const string NeedInteractions = "Need finished pair tests first.";
    public const string PairsSkipped = "Pair tests were skipped. Unknown, not a measured leftover.";
    public const string GpuHeatUnknown = "GPU Core did not heat enough to map GPU cooling.";
    public const string NeedPolicy = "Need a recommended setting first.";
    public const string NeedWatchHolds = "Need a finished Watch with three BIOS holds.";
    public const string NoLeftover = "No leftover beyond adding the two fans.";

    public static ExplanationReport Build(
        ThermalModel model,
        DiminishingReturnsReport returns,
        CoolingPolicy? policy,
        PolicyConfirmation? confirmation = null,
        DriftAssessment? drift = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(returns);

        return new ExplanationReport(
        [
            WanderSection(model.Baseline),
            PathSection(PrimaryGpuPathHeading, model, InfluenceTarget.Gpu),
            PathSection(PrimaryCpuPathHeading, model, InfluenceTarget.Cpu),
            MostEffectiveSection(model),
            DiminishingSection(returns),
            InteractionSection(model),
            RecommendedSection(policy),
            ExpectedSection(model, policy),
            ConfirmationSection(confirmation, drift),
        ]);
    }

    private static ExplanationSection WanderSection(BaselineRun? baseline)
    {
        ReferenceAssessment? assessment = ReferenceStability.FromBaseline(baseline);
        if (assessment is null)
        {
            return UnknownSection(WanderHeading, NeedWatchHolds);
        }

        return new ExplanationSection(
            WanderHeading,
            [
                WanderClaim("GPU", assessment.Gpu),
                WanderClaim("CPU", assessment.Cpu),
            ]);
    }

    private static ExplanationClaim WanderClaim(string name, SensorStabilityResult result)
    {
        string need = $"{result.MinimumDetectableCelsius:0.#} °C";
        string text = result.State switch
        {
            SensorStability.Stable =>
                $"{name} was steady. A fan has to move it by at least {need} before it counts.",
            SensorStability.Drifting =>
                $"{name} wandered {result.RangeCelsius:0.#} °C on its own. {name} cooling stays unproven.",
            SensorStability.Noisy =>
                $"{name} jumped around {result.RangeCelsius:0.#} °C. {name} cooling stays unproven.",
            _ => $"{name} temperature was missing.",
        };
        return new ExplanationClaim(
            name,
            text,
            result.State == SensorStability.Unavailable ? MetricEvidence.Unknown : MetricEvidence.Measured);
    }

    private static ExplanationSection PathSection(
        string heading,
        ThermalModel model,
        InfluenceTarget target)
    {
        IReadOnlyList<InfluenceEntry> top = TopCooling(model, target);
        if (top.Count == 0)
        {
            string missing = target == InfluenceTarget.Gpu && HasUnknownGpuHeat(model)
                ? GpuHeatUnknown
                : NeedFanTests;
            return UnknownSection(heading, missing);
        }

        string names = JoinAnd(top.Select(static entry => entry.FanGroupName));
        string targetName = InteractionBuilder.TargetName(target);
        string verb = top.Count == 1 ? "is" : "are";
        var claims = new List<ExplanationClaim>
        {
            new(
                "Path",
                $"{names} {verb} likely the main {targetName} cooling path.",
                MetricEvidence.Inferred),
        };
        foreach (InfluenceEntry entry in top)
        {
            claims.Add(new ExplanationClaim(
                entry.FanGroupName,
                FormatCooling(entry.DeltaCelsius!.Value),
                MetricEvidence.Measured));
        }

        return new ExplanationSection(heading, claims);
    }

    private static ExplanationSection MostEffectiveSection(ThermalModel model)
    {
        InfluenceEntry? best = null;
        foreach (InfluenceEntry entry in model.Influence)
        {
            if ((entry.Target != InfluenceTarget.Cpu && entry.Target != InfluenceTarget.Gpu)
                || entry.Evidence != MetricEvidence.Measured
                || entry.DeltaCelsius is not double delta
                || delta <= InfluenceMapBuilder.NoneBandCelsius)
            {
                continue;
            }

            if (best is null || delta > best.DeltaCelsius)
            {
                best = entry;
            }
        }

        if (best is null)
        {
            return UnknownSection(MostEffectiveFanHeading, NeedFanTests);
        }

        string target = InteractionBuilder.TargetName(best.Target);
        return new ExplanationSection(
            MostEffectiveFanHeading,
            [
                new ExplanationClaim(
                    best.FanGroupName,
                    $"{FormatCooling(best.DeltaCelsius!.Value)} on the {target}",
                    MetricEvidence.Measured),
            ]);
    }

    private static ExplanationSection DiminishingSection(DiminishingReturnsReport returns)
    {
        DiminishingReturnsBand? band = BestWasted(returns) ?? BestRecommended(returns);
        if (band is null)
        {
            return UnknownSection(DiminishingReturnsHeading, NeedSpeedCurve);
        }

        if (band.Evidence != MetricEvidence.Measured || band.RecommendedRpm is null)
        {
            return UnknownSection(
                DiminishingReturnsHeading,
                string.IsNullOrWhiteSpace(band.Reason) ? NeedSpeedCurve : band.Reason);
        }

        string target = InteractionBuilder.TargetName(band.Target);
        string text = band.WastedRpmMin is double wasted
            ? $"{band.FanGroupName} on the {target}: extra speed at {DiminishingReturnsAnalyzer.DescribeRpm(wasted)} and above buys little."
            : $"{band.FanGroupName} on the {target}: use about {DiminishingReturnsAnalyzer.DescribeRpm(band.RecommendedRpm.Value)}.";
        return new ExplanationSection(
            DiminishingReturnsHeading,
            [new ExplanationClaim(band.FanGroupName, text, MetricEvidence.Measured)]);
    }

    private static ExplanationSection InteractionSection(ThermalModel model)
    {
        InteractionEntry? best = null;
        foreach (InteractionEntry entry in model.Interactions)
        {
            if (entry.Evidence != MetricEvidence.Measured || entry.ResidualCelsius is not double entryResidual)
            {
                continue;
            }

            if (best is null || Math.Abs(entryResidual) > Math.Abs(best.ResidualCelsius!.Value))
            {
                best = entry;
            }
        }

        if (best is null)
        {
            string missing = model.FanTestStatus is FanTestRunStatus.Aborted or FanTestRunStatus.Cancelled
                ? PairsSkipped
                : NeedInteractions;
            return UnknownSection(DetectedInteractionHeading, missing);
        }

        string pair = $"{best.FirstGroupName} and {best.SecondGroupName}";
        string target = InteractionBuilder.TargetName(best.Target);
        double residual = best.ResidualCelsius!.Value;
        var claims = new List<ExplanationClaim>();
        if (Math.Abs(residual) < InfluenceMapBuilder.NoneBandCelsius)
        {
            claims.Add(new ExplanationClaim(
                $"{pair} on the {target}",
                NoLeftover,
                MetricEvidence.Measured));
            return new ExplanationSection(DetectedInteractionHeading, claims);
        }

        string direction = residual >= 0 ? "more together than adding them" : "less together than adding them";
        claims.Add(new ExplanationClaim(
            $"{pair} leftover on the {target}",
            $"{Math.Abs(residual):0.0} °C {direction}",
            MetricEvidence.Measured));

        string? note = best.InferredNote ?? InteractionBuilder.InferNote(residual, best.Target);
        if (!string.IsNullOrWhiteSpace(note))
        {
            claims.Add(new ExplanationClaim("Possible meaning", note, MetricEvidence.Inferred));
        }

        return new ExplanationSection(DetectedInteractionHeading, claims);
    }

    private static ExplanationSection RecommendedSection(CoolingPolicy? policy)
    {
        if (policy is null || policy.Groups.Count == 0)
        {
            return UnknownSection(RecommendedPointHeading, NeedPolicy);
        }

        var claims = new List<ExplanationClaim>(policy.Groups.Count);
        foreach (GroupPolicy group in policy.Groups)
        {
            string text = group.AppliedRpm is double rpm
                ? $"{group.DutyPercent}% duty, about {DiminishingReturnsAnalyzer.DescribeRpm(rpm)}"
                : $"{group.DutyPercent}% duty";
            claims.Add(new ExplanationClaim(group.FanGroupName, text, MetricEvidence.Modeled));
        }

        claims.Add(new ExplanationClaim(
            "Noise",
            "Fan speed stands in for loudness. That guess is inferred, not a measured sound level.",
            MetricEvidence.Inferred));
        return new ExplanationSection(RecommendedPointHeading, claims);
    }

    private static ExplanationSection ExpectedSection(ThermalModel model, CoolingPolicy? policy)
    {
        if (policy is null || policy.Groups.Count == 0)
        {
            return UnknownSection(ExpectedResultHeading, NeedPolicy);
        }

        IReadOnlyList<string> groupIds = policy.Groups.Select(static group => group.FanGroupId).ToArray();
        return new ExplanationSection(
            ExpectedResultHeading,
            [
                PredictionClaim(model, groupIds, InfluenceTarget.Gpu),
                PredictionClaim(model, groupIds, InfluenceTarget.Cpu),
                new ExplanationClaim(
                    "Load range",
                    "These tests used a lighter heat. A hotter game is modeled or unknown, not measured gaming truth.",
                    MetricEvidence.Modeled),
                new ExplanationClaim(
                    "Workload",
                    "The same tests are reused. Desktop, gaming, rendering, and mixed use come from CPU and GPU power, not a second experiment tour. A gaming result guessed from a lighter test heat is modeled, not measured gaming truth.",
                    MetricEvidence.Modeled),
            ]);
    }

    private static ExplanationSection ConfirmationSection(
        PolicyConfirmation? confirmation,
        DriftAssessment? drift)
    {
        if (confirmation is null)
        {
            return UnknownSection(ConfirmationHeading, "Apply Optimize to check the setting against a live settle.");
        }

        var claims = new List<ExplanationClaim>
        {
            new(
                "CPU",
                FormatConfirmation(confirmation.MeasuredCpuCelsius, confirmation.ExpectedCpuCelsius),
                MetricEvidence.Measured),
            new(
                "GPU",
                FormatConfirmation(confirmation.MeasuredGpuCelsius, confirmation.ExpectedGpuCelsius),
                MetricEvidence.Measured),
        };
        if (confirmation.Missed)
        {
            claims.Add(new ExplanationClaim(
                "Check",
                confirmation.AddedAirflow
                    ? "The check missed the prediction, so airflow was raised a little."
                    : "The check missed the prediction.",
                MetricEvidence.Measured));
        }
        else
        {
            claims.Add(new ExplanationClaim(
                "Check",
                "The recommended setting was applied and temperatures settled near the prediction.",
                MetricEvidence.Measured));
        }

        if (drift is { Stale: true })
        {
            if (drift.AmbientShifted && !confirmation.Missed)
            {
                claims.Add(new ExplanationClaim(
                    "Drift",
                    "The room looks different than the last stored check.",
                    MetricEvidence.Inferred));
            }

            claims.Add(new ExplanationClaim(
                "Stale",
                drift.Action == DriftAction.OfferRetest
                    ? "This setting looks stale. You can re-check the fans that look wrong."
                    : "This setting looks stale.",
                confirmation.Missed ? MetricEvidence.Measured : MetricEvidence.Inferred));
        }

        return new ExplanationSection(ConfirmationHeading, claims);
    }

    private static string FormatConfirmation(double? measured, double? expected)
    {
        if (measured is not double live)
        {
            return "unknown live temperature";
        }

        if (expected is not double target)
        {
            return $"settled at {live:0.0} °C";
        }

        return $"settled at {live:0.0} °C versus a modeled {target:0.0} °C";
    }

    private static ExplanationClaim PredictionClaim(
        ThermalModel model,
        IReadOnlyList<string> groupIds,
        InfluenceTarget target)
    {
        string title = InteractionBuilder.TargetName(target);
        ThermalPrediction prediction = model.Predict(groupIds, target);
        if (prediction.Evidence != MetricEvidence.Modeled || prediction.DeltaCelsius is not double delta)
        {
            return new ExplanationClaim(title, NeedPolicy, MetricEvidence.Unknown);
        }

        string direction = delta >= 0 ? "cooler" : "warmer";
        string vsBaseline = model.Baseline is null
            ? "if we speed those fans the same way as the tests (~25% more duty). No baseline run is stored yet"
            : $"than baseline";
        return new ExplanationClaim(
            title,
            $"about {Math.Abs(delta):0.0} °C {direction} {vsBaseline}",
            MetricEvidence.Modeled);
    }

    private static IReadOnlyList<InfluenceEntry> TopCooling(ThermalModel model, InfluenceTarget target)
    {
        var useful = new List<InfluenceEntry>();
        foreach (InfluenceEntry entry in model.Influence)
        {
            if (entry.Target != target
                || entry.Evidence != MetricEvidence.Measured
                || entry.DeltaCelsius is not double delta
                || delta <= InfluenceMapBuilder.NoneBandCelsius)
            {
                continue;
            }

            useful.Add(entry);
        }

        useful.Sort(static (left, right) =>
            right.DeltaCelsius!.Value.CompareTo(left.DeltaCelsius!.Value));

        var preferred = new List<InfluenceEntry>();
        foreach (InfluenceEntry entry in useful)
        {
            if (entry.Effect is InfluenceEffect.High or InfluenceEffect.VeryHigh)
            {
                preferred.Add(entry);
            }
        }

        IReadOnlyList<InfluenceEntry> pool = preferred.Count > 0 ? preferred : useful;
        var picked = new List<InfluenceEntry>();
        foreach (InfluenceEntry entry in pool)
        {
            if (picked.Any(existing =>
                    string.Equals(existing.FanGroupId, entry.FanGroupId, StringComparison.Ordinal)))
            {
                continue;
            }

            picked.Add(entry);
            if (picked.Count == 2)
            {
                break;
            }
        }

        return picked;
    }

    private static DiminishingReturnsBand? BestWasted(DiminishingReturnsReport returns)
    {
        DiminishingReturnsBand? best = null;
        double bestSpan = -1;
        foreach (DiminishingReturnsBand band in returns.Bands)
        {
            if (band.Evidence != MetricEvidence.Measured
                || band.WastedRpmMin is not double wastedMin)
            {
                continue;
            }

            double span = (band.WastedRpmMax ?? wastedMin) - wastedMin;
            if (best is null || span > bestSpan)
            {
                best = band;
                bestSpan = span;
            }
        }

        return best;
    }

    private static DiminishingReturnsBand? BestRecommended(DiminishingReturnsReport returns)
    {
        foreach (DiminishingReturnsBand band in returns.Bands)
        {
            if (band.Evidence == MetricEvidence.Measured && band.RecommendedRpm is not null)
            {
                return band;
            }
        }

        foreach (DiminishingReturnsBand band in returns.Bands)
        {
            return band;
        }

        return null;
    }

    private static bool HasUnknownGpuHeat(ThermalModel model)
    {
        foreach (InfluenceEntry entry in model.Influence)
        {
            if (entry.Target == InfluenceTarget.Gpu
                && entry.Evidence == MetricEvidence.Unknown
                && string.Equals(
                    entry.SkipReason,
                    FanTestReasons.GpuHeatInsufficient,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ExplanationSection UnknownSection(string heading, string text) =>
        new(heading, [new ExplanationClaim("Status", text, MetricEvidence.Unknown)]);

    private static string FormatCooling(double delta) =>
        $"{Math.Abs(delta):0.0} °C {(delta >= 0 ? "cooler" : "warmer")}";

    private static string JoinAnd(IEnumerable<string> names)
    {
        IReadOnlyList<string> list = names.ToArray();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            2 => $"{list[0]} and {list[1]}",
            _ => string.Join(", ", list),
        };
    }
}
