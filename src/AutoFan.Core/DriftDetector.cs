namespace AutoFan.Core;

public static class DriftDetector
{
    public const double AmbientShiftCelsius = 3.0;

    public static DriftAssessment Assess(
        PolicyConfirmation confirmation,
        double? referenceAmbientCelsius,
        IReadOnlyList<InfluenceEntry>? influence,
        DriftAssessment? ignored = null)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        bool ambientShifted = IsAmbientShift(referenceAmbientCelsius, confirmation.AmbientCelsius);
        bool missed = confirmation.Missed;
        bool added = confirmation.AddedAirflow;
        bool stale = ambientShifted || missed || added;
        if (!stale)
        {
            return DriftAssessment.Fresh with { AmbientCelsius = confirmation.AmbientCelsius };
        }

        string reason = Reason(ambientShifted, missed);
        IReadOnlyList<string> groupIds = RetestGroups(confirmation, influence, ambientShifted);
        DriftAction action = groupIds.Count > 0 ? DriftAction.OfferRetest : DriftAction.None;
        var assessment = new DriftAssessment(
            true,
            reason,
            action,
            groupIds,
            ambientShifted,
            missed,
            added,
            confirmation.AmbientCelsius);
        if (IsSameOffer(assessment, ignored))
        {
            return assessment with { Action = DriftAction.None };
        }

        return assessment;
    }

    public static bool IsAmbientShift(double? referenceAmbientCelsius, double? liveAmbientCelsius) =>
        referenceAmbientCelsius is double reference
        && liveAmbientCelsius is double live
        && Math.Abs(live - reference) >= AmbientShiftCelsius;

    private static string Reason(bool ambientShifted, bool missed)
    {
        if (ambientShifted && missed)
        {
            return "The room looks different than the last stored check, and the last check missed the prediction.";
        }

        if (ambientShifted)
        {
            return "The room looks different than the last stored check.";
        }

        return "The last check missed the prediction.";
    }

    private static IReadOnlyList<string> RetestGroups(
        PolicyConfirmation confirmation,
        IReadOnlyList<InfluenceEntry>? influence,
        bool ambientShifted)
    {
        if (influence is null || influence.Count == 0)
        {
            return [];
        }

        bool cpu = ambientShifted
            || PolicyConfirmer.IsMiss(confirmation.MeasuredCpuCelsius, confirmation.ExpectedCpuCelsius);
        bool gpu = ambientShifted
            || PolicyConfirmer.IsMiss(confirmation.MeasuredGpuCelsius, confirmation.ExpectedGpuCelsius);
        if (!cpu && !gpu && confirmation.AddedAirflow)
        {
            cpu = true;
            gpu = true;
        }

        var ids = new List<string>();
        if (cpu)
        {
            AddMoved(ids, influence, InfluenceTarget.Cpu);
        }

        if (gpu)
        {
            AddMoved(ids, influence, InfluenceTarget.Gpu);
        }

        return ids;
    }

    private static void AddMoved(
        List<string> ids,
        IReadOnlyList<InfluenceEntry> influence,
        InfluenceTarget target)
    {
        foreach (string id in InfluenceMapBuilder.GroupsThatMoved(influence, target))
        {
            if (!ids.Contains(id, StringComparer.Ordinal))
            {
                ids.Add(id);
            }
        }
    }

    private static bool IsSameOffer(DriftAssessment current, DriftAssessment? ignored)
    {
        if (ignored is null || !ignored.Stale)
        {
            return false;
        }

        if (current.Missed != ignored.Missed || current.AddedAirflow != ignored.AddedAirflow)
        {
            return false;
        }

        if (current.AmbientShifted != ignored.AmbientShifted)
        {
            return false;
        }

        if (current.AmbientShifted
            && IsAmbientShift(ignored.AmbientCelsius, current.AmbientCelsius))
        {
            return false;
        }

        return true;
    }
}
