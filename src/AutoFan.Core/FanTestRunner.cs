namespace AutoFan.Core;

public sealed class FanTestRunner
{
    private readonly IHardwareBackend _hardware;
    private readonly IWorkloadActuator _workload;
    private readonly ICompetingSoftwareScanner _scanner;
    private readonly IFanTestStore _store;
    private readonly TimeProvider _clock;
    private readonly FanTestSchedule _schedule;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ThermalTrend _trend = new();
    private readonly ThermalAbortLimits _limits;
    private readonly FanPresence? _presence;
    private readonly bool _gpuHeatUseful;
    private readonly ReferenceAssessment? _stability;
    private readonly HeatProfile? _lowHeat;
    private readonly HeatProfile? _everydayHeat;
    private readonly bool _everydayGpuHeatUseful;
    private readonly BaselineRun? _baseline;

    public FanTestRunner(
        IHardwareBackend hardware,
        IWorkloadActuator workload,
        ICompetingSoftwareScanner scanner,
        IFanTestStore store,
        TimeProvider? clock = null,
        FanTestSchedule? schedule = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ThermalAbortLimits? limits = null,
        FanPresence? presence = null,
        bool gpuHeatUseful = true,
        ReferenceAssessment? stability = null,
        HeatProfile? lowHeat = null,
        HeatProfile? everydayHeat = null,
        bool everydayGpuHeatUseful = false,
        BaselineRun? baseline = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _workload = workload ?? throw new ArgumentNullException(nameof(workload));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
        _schedule = schedule ?? FanTestSchedule.Default;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _limits = ThermalAbortLimits.FromUser(limits?.CpuCelsius, limits?.GpuCelsius);
        _presence = presence is { Completed: true } ? presence : null;
        _gpuHeatUseful = gpuHeatUseful;
        _stability = stability;
        _lowHeat = lowHeat;
        _everydayHeat = everydayHeat;
        _everydayGpuHeatUseful = everydayGpuHeatUseful;
        _baseline = baseline;
    }

    public async Task<FanTestRun> RunAsync(
        CancellationToken cancellationToken = default,
        IProgress<FanTestProgress>? progress = null,
        IReadOnlyList<string>? onlyGroupIds = null)
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset startedAt = _clock.GetUtcNow();
        var samples = new List<FanTestSample>();
        var unknown = new List<InfluenceEntry>();
        var skipped = new List<SkippedFanGroup>();

        try
        {
            if (_lowHeat is not HeatProfile lowHeat)
            {
                _workload.Stop();
                _hardware.RestoreDefaults();
                return Persist(
                    id,
                    startedAt,
                    samples,
                    unknown,
                    skipped,
                    FanTestRunStatus.Aborted,
                    HeatProfile.MissingLampDetail);
            }

            IReadOnlyList<FanGroup> groups = _hardware.FanGroups.ToArray();
            var candidates = new List<FanGroup>();
            foreach (FanGroup group in groups)
            {
                if (group.Kind == FanGroupKind.Pump)
                {
                    skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.Pump));
                    continue;
                }

                if (FanWriteCandidates.IsGpuHeader(group) && !FanWriteCandidates.IsWritableNvidiaGpuFan(group))
                {
                    skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.Gpu));
                    continue;
                }

                if (!FanWriteCandidates.IsWritableTestFan(group))
                {
                    if (group.Kind != FanGroupKind.Fan)
                    {
                        continue;
                    }

                    if (!group.IsControllable)
                    {
                        skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.NotControllable));
                    }

                    continue;
                }

                if (!_gpuHeatUseful && FanWriteCandidates.IsWritableNvidiaGpuFan(group))
                {
                    skipped.Add(new SkippedFanGroup(
                        group.Id,
                        group.Name,
                        FanTestReasons.GpuHeatInsufficient));
                    continue;
                }

                if (!FanWriteCandidates.HasUsableTachometer(group, _presence))
                {
                    skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.NoRpm));
                    continue;
                }

                candidates.Add(group);
            }

            if (onlyGroupIds is { Count: > 0 })
            {
                var allowed = new HashSet<string>(onlyGroupIds, StringComparer.Ordinal);
                candidates.RemoveAll(group => !allowed.Contains(group.Id));
            }

            candidates = ScreenOrderer.Order(candidates, CasePriorCatalog.GenericMidTower).ToList();
            IReadOnlyList<FanGroup> representatives = FanWriteCandidates.TakeOnePerCoupledSet(candidates, _presence);
            var representativeIds = representatives
                .Select(static group => group.Id)
                .ToHashSet(StringComparer.Ordinal);
            foreach (FanGroup group in candidates)
            {
                if (representativeIds.Contains(group.Id) || FanWriteCandidates.CoupledSetKey(group) is null)
                {
                    continue;
                }

                skipped.Add(new SkippedFanGroup(group.Id, group.Name, FanTestReasons.Coupled));
            }

            candidates = representatives.ToList();

            string? waitAbort = await WaitToCoolAsync(startedAt, progress, cancellationToken).ConfigureAwait(false);
            if (waitAbort is not null)
            {
                _workload.Stop();
                _hardware.RestoreDefaults();
                return Persist(
                    id,
                    startedAt,
                    samples,
                    unknown,
                    skipped,
                    FanTestRunStatus.Aborted,
                    waitAbort);
            }

            _workload.ApplyLow(lowHeat);
            var testedDuties = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            for (int index = 0; index < candidates.Count; index++)
            {
                FanGroup group = FindGroup(candidates[index].Id) ?? candidates[index];
                string? abort = await TestGroupAsync(
                    group,
                    index,
                    candidates.Count,
                    FanTestSchedule.ScreenDuties,
                    FanTestStage.Screen,
                    captureReference: true,
                    startedAt,
                    HeatId.Low,
                    samples,
                    unknown,
                    skipped,
                    testedDuties,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                if (abort is not null)
                {
                    _workload.Stop();
                    _hardware.RestoreDefaults();
                    return Persist(
                        id,
                        startedAt,
                        samples,
                        unknown,
                        skipped,
                        FanTestRunStatus.Aborted,
                        abort);
                }
            }

            IReadOnlyList<InfluenceEntry> preview = ApplyGates(
                InfluenceMapBuilder.Build(ForHeat(samples, HeatId.Low), _stability));
            var refine = new List<FanGroup>();
            foreach (FanGroup group in candidates)
            {
                if (InfluenceMapBuilder.MovedATemperature(preview, group.Id))
                {
                    refine.Add(FindGroup(group.Id) ?? group);
                }
            }

            for (int index = 0; index < refine.Count; index++)
            {
                FanGroup group = refine[index];
                string? abort = await TestGroupAsync(
                    group,
                    index,
                    refine.Count,
                    FanTestSchedule.RefineDuties,
                    FanTestStage.Refine,
                    captureReference: false,
                    startedAt,
                    HeatId.Low,
                    samples,
                    unknown,
                    skipped,
                    testedDuties,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                if (abort is not null)
                {
                    _workload.Stop();
                    _hardware.RestoreDefaults();
                    return Persist(
                        id,
                        startedAt,
                        samples,
                        unknown,
                        skipped,
                        FanTestRunStatus.Aborted,
                        abort);
                }
            }

            IReadOnlyList<InfluenceEntry> lowMap = ApplyGates(
                InfluenceMapBuilder.Build(ForHeat(samples, HeatId.Low), _stability));
            string? everydayFatal = await TryEverydayAsync(
                samples,
                unknown,
                skipped,
                lowMap,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (everydayFatal is not null)
            {
                _workload.Stop();
                _hardware.RestoreDefaults();
                return Persist(
                    id,
                    startedAt,
                    samples,
                    unknown,
                    skipped,
                    FanTestRunStatus.Aborted,
                    everydayFatal);
            }

            _workload.Stop();
            _hardware.RestoreDefaults();
            return Persist(
                id,
                startedAt,
                samples,
                unknown,
                skipped,
                FanTestRunStatus.Completed,
                abortDetail: null);
        }
        catch (OperationCanceledException)
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            return Persist(
                id,
                startedAt,
                samples,
                unknown,
                skipped,
                FanTestRunStatus.Cancelled,
                "Fan tests cancelled.");
        }
        catch
        {
            _workload.Stop();
            _hardware.RestoreDefaults();
            throw;
        }
    }

    private async Task<string?> TestGroupAsync(
        FanGroup group,
        int index,
        int count,
        IReadOnlyList<int> duties,
        FanTestStage stage,
        bool captureReference,
        DateTimeOffset stageStartedAt,
        HeatId heatId,
        List<FanTestSample> samples,
        List<InfluenceEntry> unknown,
        List<SkippedFanGroup> skipped,
        Dictionary<string, HashSet<int>> testedDuties,
        IProgress<FanTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        int currentDuty = group.DutyCyclePercent ?? 40;
        IReadOnlyList<int> targets = FanTestSchedule.AbsoluteDuties(
            duties,
            currentDuty,
            testedDuties.GetValueOrDefault(group.Id));
        if (targets.Count == 0)
        {
            if (captureReference && FanTestSchedule.IsAlreadyAtMax(currentDuty, targets))
            {
                unknown.AddRange(InfluenceMapBuilder.UnknownForGroup(
                    group.Id,
                    group.Name,
                    FanTestReasons.AlreadyAtMaxDuty,
                    group.DutyCyclePercent,
                    group.Rpm));
            }

            return null;
        }

        if (captureReference)
        {
            string? holdAbort = await SampleAsync(
                group,
                index,
                count,
                FanTestStage.Reference,
                stageStartedAt,
                heatId,
                samples,
                session: null,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (holdAbort is not null)
            {
                return holdAbort;
            }
        }

        for (int dutyIndex = 0; dutyIndex < targets.Count; dutyIndex++)
        {
            FanGroup live = FindGroup(group.Id) ?? group;
            int duty = targets[dutyIndex];
            FanTestStage sampleStage = stage == FanTestStage.Screen && dutyIndex == 0
                ? FanTestStage.Perturb
                : stage;
            string? abort = await WriteAndSampleAsync(
                live,
                duty,
                index,
                count,
                sampleStage,
                stageStartedAt,
                heatId,
                samples,
                skipped,
                testedDuties,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return abort;
            }
        }

        return null;
    }

    private async Task<string?> WriteAndSampleAsync(
        FanGroup group,
        int duty,
        int index,
        int count,
        FanTestStage stage,
        DateTimeOffset stageStartedAt,
        HeatId heatId,
        List<FanTestSample> samples,
        List<SkippedFanGroup> skipped,
        Dictionary<string, HashSet<int>> testedDuties,
        IProgress<FanTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var session = new SafeFanSession(_hardware, _scanner, _clock, limits: _limits);
        DutySetResult write = session.TrySetDuty(group.Id, duty);
        if (!write.Accepted)
        {
            if (session.IsAborted || IsRunAbort(write.Error))
            {
                return write.Error ?? session.AbortDetail ?? "Fan test write was rejected.";
            }

            skipped.Add(new SkippedFanGroup(
                group.Id,
                group.Name,
                write.Error ?? FanTestReasons.NotControllable));
            return null;
        }

        HardwareSnapshot live = _hardware.ReadSnapshot();
        FanGroup? afterWrite = live.FanGroups.FirstOrDefault(fan => fan.Id == group.Id);
        if (afterWrite is not null && !FanWriteCandidates.HasTachometer(afterWrite))
        {
            MarkDuty(testedDuties, group.Id, duty);
            return null;
        }

        string? sampled = await SampleAsync(
            group,
            index,
            count,
            stage,
            stageStartedAt,
            heatId,
            samples,
            session,
            progress,
            cancellationToken).ConfigureAwait(false);
        MarkDuty(testedDuties, group.Id, duty);
        return sampled;
    }

    private static void MarkDuty(Dictionary<string, HashSet<int>> testedDuties, string groupId, int duty)
    {
        if (!testedDuties.TryGetValue(groupId, out HashSet<int>? seen))
        {
            seen = [];
            testedDuties[groupId] = seen;
        }

        seen.Add(duty);
    }

    private async Task<string?> SampleAsync(
        FanGroup group,
        int index,
        int count,
        FanTestStage stage,
        DateTimeOffset stageStartedAt,
        HeatId heatId,
        List<FanTestSample> samples,
        SafeFanSession? session,
        IProgress<FanTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<HardwareSnapshot>();
        int holdStart = samples.Count;
        DateTimeOffset deadline = _clock.GetUtcNow() + _schedule.Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clock.GetUtcNow() - stageStartedAt >= TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes))
            {
                return $"Fan tests reached the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.";
            }

            session?.CheckLimits();
            if (session is { IsAborted: true })
            {
                return session.AbortDetail ?? "Fan test aborted by safety limits.";
            }

            HardwareSnapshot snapshot = _hardware.ReadSnapshot();
            DateTimeOffset now = _clock.GetUtcNow();
            _trend.Add(now, snapshot);
            if (_trend.Evaluate(_limits) is ThermalAbortReason rise)
            {
                return SafetyLimits.Describe(rise);
            }

            samples.Add(new FanTestSample(now, group.Id, group.Name, stage, snapshot, Settled: false, HeatId: heatId));
            snapshots.Add(snapshot);
            progress?.Report(new FanTestProgress(
                group.Id,
                group.Name,
                index + 1,
                count,
                stage,
                snapshot,
                Describe(group.Name, index + 1, count, stage, heatId)));

            ThermalAbortReason? abort = SafetyLimits.EvaluateWhileHeating(snapshot, _workload, _limits)
                ?? _trend.Evaluate(_limits);
            if (abort is not null)
            {
                return SafetyLimits.Describe(abort.Value, _limits);
            }

            if (TemperatureSettle.RelevantTempsSettled(snapshots))
            {
                MarkSettled(samples, holdStart);
                return null;
            }

            if (now >= deadline)
            {
                return null;
            }

            await _delay(_schedule.SamplePeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> WaitToCoolAsync(
        DateTimeOffset stageStartedAt,
        IProgress<FanTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _clock.GetUtcNow() + ExperimentStartGate.Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clock.GetUtcNow() - stageStartedAt >= TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes))
            {
                return $"Fan tests reached the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.";
            }

            HardwareSnapshot snapshot = _hardware.ReadSnapshot();
            _trend.Add(_clock.GetUtcNow(), snapshot);
            ThermalAbortReason? abort = SafetyLimits.Evaluate(snapshot, _limits) ?? _trend.Evaluate(_limits);
            if (abort is not null)
            {
                return SafetyLimits.Describe(abort.Value, _limits);
            }

            if (ExperimentStartGate.IsReady(snapshot, out string detail))
            {
                return null;
            }

            if (_clock.GetUtcNow() >= deadline)
            {
                return "CPU or GPU stayed above "
                    + $"{ExperimentStartGate.CpuReadyCelsius} °C for {ExperimentStartGate.Timeout.TotalMinutes:0} minutes. "
                    + "Wait to cool, then try again.";
            }

            progress?.Report(new FanTestProgress(
                string.Empty,
                string.Empty,
                0,
                0,
                FanTestStage.Reference,
                snapshot,
                detail));
            await _delay(_schedule.SamplePeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> TryEverydayAsync(
        List<FanTestSample> samples,
        List<InfluenceEntry> unknown,
        List<SkippedFanGroup> skipped,
        IReadOnlyList<InfluenceEntry> lowInfluence,
        IProgress<FanTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FanGroup> movers = LowMovers(lowInfluence);
        if (movers.Count == 0)
        {
            return null;
        }

        _workload.Stop();
        _hardware.RestoreDefaults();
        DateTimeOffset stageStartedAt = _clock.GetUtcNow();

        if (_everydayHeat is not HeatProfile everydayHeat)
        {
            progress?.Report(new FanTestProgress(
                string.Empty,
                string.Empty,
                0,
                0,
                FanTestStage.Reference,
                _hardware.ReadSnapshot(),
                HeatProfile.MissingLampDetail));
            return null;
        }

        if (!IdleAlreadySettled())
        {
            FanGroup idleGroup = FindGroup(movers[0].Id) ?? movers[0];
            progress?.Report(new FanTestProgress(
                idleGroup.Id,
                idleGroup.Name,
                0,
                movers.Count,
                FanTestStage.Reference,
                _hardware.ReadSnapshot(),
                "Sitting idle with no fan writes. Everyday heat comes next."));
            string? idleAbort = await SampleAsync(
                idleGroup,
                0,
                movers.Count,
                FanTestStage.Reference,
                stageStartedAt,
                HeatId.Idle,
                samples,
                session: null,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (idleAbort is not null)
            {
                return IsEverydayStageSkip(idleAbort) ? null : idleAbort;
            }
        }

        string? waitAbort = await WaitToCoolAsync(stageStartedAt, progress, cancellationToken).ConfigureAwait(false);
        if (waitAbort is not null)
        {
            return IsEverydayStageSkip(waitAbort) ? null : waitAbort;
        }

        _workload.ApplyEveryday(everydayHeat);
        var testedDuties = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var everydayCandidates = new List<FanGroup>();
        foreach (FanGroup mover in movers)
        {
            FanGroup live = FindGroup(mover.Id) ?? mover;
            if (!_everydayGpuHeatUseful && FanWriteCandidates.IsWritableNvidiaGpuFan(live))
            {
                skipped.Add(new SkippedFanGroup(
                    live.Id,
                    live.Name,
                    FanTestReasons.GpuHeatInsufficient));
                continue;
            }

            everydayCandidates.Add(live);
        }

        for (int index = 0; index < everydayCandidates.Count; index++)
        {
            FanGroup group = everydayCandidates[index];
            progress?.Report(new FanTestProgress(
                group.Id,
                group.Name,
                index + 1,
                everydayCandidates.Count,
                FanTestStage.Screen,
                _hardware.ReadSnapshot(),
                Describe(group.Name, index + 1, everydayCandidates.Count, FanTestStage.Screen, HeatId.Everyday)));
            string? abort = await TestGroupAsync(
                group,
                index,
                everydayCandidates.Count,
                FanTestSchedule.EverydayScreenDuties,
                FanTestStage.Screen,
                captureReference: true,
                stageStartedAt,
                HeatId.Everyday,
                samples,
                unknown,
                skipped,
                testedDuties,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return IsEverydayStageSkip(abort) ? null : abort;
            }
        }

        IReadOnlyList<InfluenceEntry> everydayPreview = ApplyEverydayGates(
            InfluenceMapBuilder.Build(ForHeat(samples, HeatId.Everyday), _stability));
        var refine = new List<FanGroup>();
        foreach (FanGroup group in everydayCandidates)
        {
            if (InfluenceMapBuilder.MovedATemperature(everydayPreview, group.Id))
            {
                refine.Add(FindGroup(group.Id) ?? group);
            }
        }

        for (int index = 0; index < refine.Count; index++)
        {
            FanGroup group = refine[index];
            string? abort = await TestGroupAsync(
                group,
                index,
                refine.Count,
                FanTestSchedule.EverydayRefineDuties,
                FanTestStage.Refine,
                captureReference: false,
                stageStartedAt,
                HeatId.Everyday,
                samples,
                unknown,
                skipped,
                testedDuties,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return IsEverydayStageSkip(abort) ? null : abort;
            }
        }

        return null;
    }

    private IReadOnlyList<FanGroup> LowMovers(IReadOnlyList<InfluenceEntry> lowInfluence)
    {
        var movers = new List<FanGroup>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (InfluenceEntry entry in lowInfluence)
        {
            if (!seen.Add(entry.FanGroupId)
                || !InfluenceMapBuilder.MovedATemperature(lowInfluence, entry.FanGroupId))
            {
                continue;
            }

            FanGroup? group = FindGroup(entry.FanGroupId);
            if (group is not null)
            {
                movers.Add(group);
            }
        }

        return movers;
    }

    private bool IdleAlreadySettled()
    {
        if (_baseline is null)
        {
            return false;
        }

        HardwareSnapshot[] idle = _baseline.Samples
            .Where(sample => sample.Phase == BaselinePhase.Idle)
            .Select(sample => sample.Snapshot)
            .ToArray();
        return TemperatureSettle.RelevantTempsSettled(idle);
    }

    private static IReadOnlyList<FanTestSample> ForHeat(IReadOnlyList<FanTestSample> samples, HeatId heatId) =>
        samples.Where(sample => sample.HeatId == heatId).ToArray();

    private static bool IsEverydayStageSkip(string abort)
    {
        if (abort.Contains("minute abort limit", StringComparison.Ordinal)
            || abort.Contains("stayed above", StringComparison.Ordinal))
        {
            return true;
        }

        ThermalAbortReason? reason = SafetyLimits.TryParse(abort);
        return reason is ThermalAbortReason.CpuOverLimit
            or ThermalAbortReason.GpuOverLimit
            or ThermalAbortReason.OtherSensorOverLimit
            or ThermalAbortReason.RateOfRise;
    }

    private IReadOnlyList<InfluenceEntry> ApplyEverydayGates(IReadOnlyList<InfluenceEntry> entries)
    {
        IReadOnlyList<InfluenceEntry> gated = entries;
        if (!_everydayGpuHeatUseful)
        {
            gated = InfluenceMapBuilder.WithoutGpuTargets(gated, FanTestReasons.GpuHeatInsufficient);
        }

        return ReferenceStability.WithoutUnusableTargets(gated, _stability);
    }

    private IReadOnlyList<InfluenceEntry> ApplyGates(IReadOnlyList<InfluenceEntry> entries)
    {
        IReadOnlyList<InfluenceEntry> gated = entries;
        if (!_gpuHeatUseful)
        {
            gated = InfluenceMapBuilder.WithoutGpuTargets(gated, FanTestReasons.GpuHeatInsufficient);
        }

        return ReferenceStability.WithoutUnusableTargets(gated, _stability);
    }

    private FanGroup? FindGroup(string id) =>
        _hardware.FanGroups.FirstOrDefault(fan => fan.Id == id);

    private static void MarkSettled(List<FanTestSample> samples, int fromIndex)
    {
        for (int index = fromIndex; index < samples.Count; index++)
        {
            samples[index] = samples[index] with { Settled = true };
        }
    }

    private static bool IsRunAbort(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.Contains("Competing fan software", StringComparison.Ordinal)
            || error.Contains("abort limit", StringComparison.Ordinal);
    }

    private FanTestRun Persist(
        Guid id,
        DateTimeOffset startedAt,
        IReadOnlyList<FanTestSample> samples,
        IReadOnlyList<InfluenceEntry> unknown,
        IReadOnlyList<SkippedFanGroup> skipped,
        FanTestRunStatus status,
        string? abortDetail)
    {
        IReadOnlyList<InfluenceEntry> measured = ApplyGates(
            InfluenceMapBuilder.Build(ForHeat(samples, HeatId.Low), _stability));
        var influence = new List<InfluenceEntry>(measured.Count + unknown.Count);
        influence.AddRange(measured);
        foreach (InfluenceEntry entry in unknown)
        {
            if (!influence.Any(existing =>
                    existing.FanGroupId == entry.FanGroupId
                    && existing.Target == entry.Target))
            {
                influence.Add(entry);
            }
        }

        var run = new FanTestRun(
            id,
            startedAt,
            _clock.GetUtcNow(),
            status,
            abortDetail,
            _workload.GpuLoadAvailable,
            samples.ToArray(),
            influence,
            skipped.ToArray());
        _store.Save(run);
        return run;
    }

    private string Describe(string name, int index, int count, FanTestStage stage, HeatId heatId)
    {
        string priorNote = CaseZoneMatcher.FromName(name) == CaseZone.Unknown
            ? string.Empty
            : " The case prior expected this header to matter; it still gets a real measurement.";
        string heatNote = heatId == HeatId.Everyday
            ? " Everyday heat is on. "
            : " ";
        return stage switch
        {
            FanTestStage.Reference when heatId == HeatId.Idle =>
                "Sitting idle. Fans still on BIOS. No fan writes.",
            FanTestStage.Reference =>
                $"Measuring normal BIOS speeds before changing {name}.{heatNote}Fan {index} of {count}. Waiting until temperatures stop moving. Fans still on BIOS.",
            FanTestStage.Perturb or FanTestStage.Screen =>
                $"Screening {name} at a few speeds.{heatNote}{priorNote} It goes back to BIOS after. Fan {index} of {count}. Waiting until temperatures stop moving.",
            FanTestStage.Refine =>
                $"Refining {name} at extra speeds.{heatNote}It goes back to BIOS after. Fan {index} of {count}. Waiting until temperatures stop moving.",
            _ => $"{name} ({index} of {count})",
        };
    }
}
