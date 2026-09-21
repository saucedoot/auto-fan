namespace AutoFan.Core;

/// <summary>
/// Whether the Optimize walk may Continue after a step, or must retry.
/// </summary>
public static class OptimizeWalkOutcome
{
    public const string EmptyFanTestsDetail =
        "No fan measurements yet. Retry when you are ready.";

    public const string SkipPairsDetail =
        "Fan tests stopped before pair tests. Pair tests were skipped. Continue to hold a quieter setting from what was measured.";

    public const string PairsFinishedDetail =
        "Fan tests finished. Continue when you are ready.";

    public const string WatchGpuCoolNote =
        " GPU Core did not heat enough, so GPU cooling stays Unknown.";

    public static bool ContinueAfterWatch(BaselineRunStatus status) =>
        status == BaselineRunStatus.Completed;

    public static bool HasUsefulInfluence(IReadOnlyList<InfluenceEntry>? influence)
    {
        if (influence is null)
        {
            return false;
        }

        foreach (InfluenceEntry entry in influence)
        {
            if (entry.Evidence == MetricEvidence.Measured && entry.DeltaCelsius is not null)
            {
                return true;
            }
        }

        return false;
    }

    public static WalkFansNext AfterFans(FanTestRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (IsStopWalkAbort(run.AbortDetail))
        {
            return WalkFansNext.Retry;
        }

        bool useful = HasUsefulInfluence(run.Influence);
        if (run.Status == FanTestRunStatus.Completed)
        {
            return useful ? WalkFansNext.RunPairs : WalkFansNext.Retry;
        }

        return useful ? WalkFansNext.SkipPairs : WalkFansNext.Retry;
    }

    public static bool ContinueAfterFans(FanTestRun run) =>
        AfterFans(run) != WalkFansNext.Retry;

    public static bool ContinueAfterFans(
        FanTestRunStatus status,
        IReadOnlyList<InfluenceEntry>? influence,
        string? abortDetail = null)
    {
        return ContinueAfterFans(
            new FanTestRun(
                Guid.Empty,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                status,
                abortDetail,
                GpuLoadAvailable: false,
                [],
                influence ?? [],
                []));
    }

    public static bool ShouldRunPairs(FanTestRun run) =>
        AfterFans(run) == WalkFansNext.RunPairs;

    public static string WatchCompleteDetail(BaselineRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var parts = new List<string> { "Watching finished." };
        if (!GpuHeat.IsUseful(run))
        {
            parts.Add(WatchGpuCoolNote.Trim());
        }

        if (ReferenceStability.FromBaseline(run) is ReferenceAssessment assessment)
        {
            parts.Add(assessment.WatchNote);
        }

        parts.Add("Continue when you are ready.");
        return string.Join(" ", parts);
    }

    public static string FansCompleteDetail(FanTestRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return AfterFans(run) == WalkFansNext.SkipPairs
            ? SkipPairsDetail
            : PairsFinishedDetail;
    }

    public static string WatchFailDetail(BaselineRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return string.IsNullOrWhiteSpace(run.AbortDetail)
            ? "Watching stopped before it finished. Retry when you are ready."
            : run.AbortDetail;
    }

    public static string FansFailDetail(FanTestRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!string.IsNullOrWhiteSpace(run.AbortDetail))
        {
            return run.AbortDetail;
        }

        return EmptyFanTestsDetail;
    }

    public static bool IsStopWalkAbort(string? detail)
    {
        ThermalAbortReason? reason = SafetyLimits.TryParse(detail);
        if (reason is ThermalAbortReason.TelemetryLost or ThermalAbortReason.GpuDeviceLost)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(detail)
            && detail.Contains("Competing fan software", StringComparison.Ordinal);
    }
}
