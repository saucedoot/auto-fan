namespace AutoFan.Core;

public sealed record TraceReplayResult(
    HoldAssessment AtFiveSeconds,
    HoldAssessment AtEnd,
    bool CalledCooling);

/// <summary>
/// Plays a recorded trace through the hold check. It does not lock the
/// provisional slope or power numbers.
/// </summary>
public static class TraceReplayer
{
    public static TraceReplayResult Replay(
        IReadOnlyList<HardwareSnapshot> trace,
        bool dutyCommanded = false,
        string? fanGroupId = null,
        bool timedOut = false)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (trace.Count == 0)
        {
            throw new ArgumentException("A trace needs at least one snapshot.", nameof(trace));
        }

        DateTimeOffset start = trace[0].CapturedAt;
        int fiveIndex = 0;
        for (int index = 0; index < trace.Count; index++)
        {
            fiveIndex = index;
            if ((trace[index].CapturedAt - start).TotalSeconds >= 5)
            {
                break;
            }
        }

        HardwareSnapshot[] prefix = trace.Take(fiveIndex + 1).ToArray();
        HoldAssessment early = HoldAssessor.Evaluate(prefix, timedOut: false, fanGroupId, dutyCommanded);
        HoldAssessment end = HoldAssessor.Evaluate(trace, timedOut, fanGroupId, dutyCommanded);
        bool cooling = end == HoldAssessment.SettledMeasured && TemperatureFell(trace);
        return new TraceReplayResult(early, end, cooling);
    }

    private static bool TemperatureFell(IReadOnlyList<HardwareSnapshot> trace)
    {
        double? first = PreferredTemperature.Read(trace[0], SensorKind.CpuTemperature);
        double? last = PreferredTemperature.Read(trace[^1], SensorKind.CpuTemperature);
        return first is double from && last is double to && to < from - 0.2;
    }
}
