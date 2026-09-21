using System.Globalization;
using System.Text;

namespace AutoFan.Core;

public sealed record ConfirmationDiagnosis(
    InfluenceTarget Target,
    double? ConfirmationStartCelsius,
    double? IdleCelsius,
    PredictionBreakdown Breakdown,
    double? PredictedCelsius,
    double? MeasuredCelsius,
    double? ErrorCelsius)
{
    public static ConfirmationDiagnosis From(
        ThermalModel model,
        PolicyConfirmation confirmation,
        IReadOnlyList<string> groupIds,
        double? idleCelsius)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(groupIds);

        PredictionBreakdown breakdown = model.Explain(groupIds, InfluenceTarget.Gpu);
        double? predicted = confirmation.ExpectedGpuCelsius;
        double? measured = confirmation.MeasuredGpuCelsius;
        double? start = predicted is double expected && breakdown.TotalDeltaCelsius is double delta
            ? expected + delta
            : null;
        double? error = measured is double live && predicted is double guess
            ? live - guess
            : null;
        return new ConfirmationDiagnosis(
            InfluenceTarget.Gpu,
            start,
            idleCelsius,
            breakdown,
            predicted,
            measured,
            error);
    }

    public static double? MeanPhaseTemperature(
        BaselineRun? baseline,
        BaselinePhase phase,
        SensorKind kind)
    {
        if (baseline is null)
        {
            return null;
        }

        var values = new List<double>();
        foreach (BaselineSample sample in baseline.Samples)
        {
            if (sample.Phase != phase)
            {
                continue;
            }

            if (PreferredTemperature.Read(sample.Snapshot, kind) is double value)
            {
                values.Add(value);
            }
        }

        return values.Count == 0 ? null : values.Average();
    }

    public string Format()
    {
        var text = new StringBuilder();
        text.AppendLine("GPU prediction");
        text.AppendLine("--------------");
        text.AppendLine($"Confirmation-start GPU:    {FormatTemp(ConfirmationStartCelsius)}");
        text.AppendLine($"Watch idle GPU (compare):  {FormatTemp(IdleCelsius)}");
        foreach (PredictionContribution fan in Breakdown.Fans)
        {
            text.AppendLine($"Predicted contribution {fan.Name}: {FormatDelta(fan.DeltaCelsius)}");
        }

        if (Breakdown.Fans.Count == 0)
        {
            text.AppendLine("Predicted contribution (fans): unknown");
        }

        foreach (PredictionContribution pair in Breakdown.Pairs)
        {
            text.AppendLine($"Pair leftover {pair.Name}: {FormatDelta(pair.DeltaCelsius)}");
        }

        text.AppendLine($"Pair leftovers total:      {FormatDelta(Breakdown.PairTotalCelsius)}");
        text.AppendLine($"Other corrections:         {PredictionBreakdown.NoOtherCorrections}");
        text.AppendLine("--------------------------------");
        text.AppendLine($"Predicted delta:           {FormatDelta(Breakdown.TotalDeltaCelsius)}");
        text.AppendLine($"Predicted:                 {FormatTemp(PredictedCelsius)}");
        text.AppendLine($"Actual confirmation:       {FormatTemp(MeasuredCelsius)}");
        text.AppendLine($"Error:                     {FormatError(ErrorCelsius)}");
        return text.ToString().TrimEnd();
    }

    private static string FormatTemp(double? value) =>
        value is double temp
            ? string.Create(CultureInfo.InvariantCulture, $"{temp:0.0}°C")
            : "unknown";

    private static string FormatDelta(double? value) =>
        value is double delta
            ? string.Create(CultureInfo.InvariantCulture, $"{-delta:0.0}°C")
            : "unknown";

    private static string FormatError(double? value) =>
        value is double error
            ? string.Create(CultureInfo.InvariantCulture, $"{error:+0.0;-0.0;0.0}°C")
            : "unknown";
}
