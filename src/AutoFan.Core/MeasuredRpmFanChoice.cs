namespace AutoFan.Core;

public sealed record MeasuredRpmFanChoice(string FanGroupId, string FanGroupName);

public sealed record MeasuredRpmHeatChoice(HeatId Heat, string Label)
{
    public static IReadOnlyList<MeasuredRpmHeatChoice> From(IReadOnlyList<FanSpeedCurve> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);
        var choices = new List<MeasuredRpmHeatChoice>();
        foreach (HeatId heat in Enum.GetValues<HeatId>())
        {
            if (curves.Any(curve => curve.Heat == heat && curve.Points.Count > 0))
            {
                choices.Add(new MeasuredRpmHeatChoice(heat, LabelFor(heat)));
            }
        }

        return choices;
    }

    public static string LabelFor(HeatId heat) =>
        heat switch
        {
            HeatId.Idle => "Idle",
            HeatId.Everyday => "Everyday",
            HeatId.Low => "Low",
            HeatId.Hot => "Hot",
            _ => heat.ToString(),
        };
}
