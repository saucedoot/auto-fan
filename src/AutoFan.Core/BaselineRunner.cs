namespace AutoFan.Core;

public sealed class BaselineRunner
{
    private readonly IHardwareBackend _hardware;
    private readonly IWorkloadActuator _workload;
    private readonly IBaselineStore _store;
    private readonly TimeProvider _clock;
    private readonly BaselineSchedule _schedule;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ThermalAbortLimits _limits;
    private PumpWatch? _pumps;
    private readonly List<HeatAnchor> _anchors = [];
    private HoldAssessment _lastAssessment = HoldAssessment.TransientModeled;
    private HardwareSnapshot? _lastReference;

    public BaselineRunner(
        IHardwareBackend hardware,
        IWorkloadActuator workload,
        IBaselineStore store,
        TimeProvider? clock = null,
        BaselineSchedule? schedule = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ThermalAbortLimits? limits = null)
    {
        _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        _workload = workload ?? throw new ArgumentNullException(nameof(workload));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
        _schedule = schedule ?? BaselineSchedule.Default;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _limits = ThermalAbortLimits.FromUser(limits?.CpuCelsius, limits?.GpuCelsius);
    }

    public async Task<BaselineRun> RunAsync(
        CancellationToken cancellationToken = default,
        IProgress<BaselineProgress>? progress = null)
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset startedAt = _clock.GetUtcNow();
        var samples = new List<BaselineSample>();
        _anchors.Clear();
        _pumps = null;

        try
        {
            string? abort = await RunPhaseAsync(
                BaselinePhase.Idle,
                WorkloadLevel.Idle,
                _schedule.Idle,
                startedAt,
                samples,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return StopAndPersist(id, startedAt, samples, BaselineRunStatus.Aborted, abort);
            }

            _workload.ApplyEveryday(HeatProfile.CpuOnly);
            abort = await RunAnchorHoldAsync(
                BaselinePhase.Everyday,
                startedAt,
                samples,
                "Adding CPU-only heat with the safe worker count. GPU work is off. Fans are not being changed.",
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return StopAndPersist(id, startedAt, samples, BaselineRunStatus.Aborted, abort);
            }

            _anchors.Add(new HeatAnchor(
                HeatAnchorKind.Cpu,
                HeatProfile.CpuOnly,
                _lastAssessment,
                _lastReference,
                GpuRiseCelsius: null));

            double? idleGpu = IdleGpu(samples);
            HeatCalibrationResult calibration = await HeatCalibrator.RunAsync(
                _hardware,
                _workload,
                idleGpu,
                _limits,
                _clock,
                startedAt,
                _schedule.SamplePeriod,
                _delay,
                progress,
                cancellationToken,
                HeatProfile.GpuStart).ConfigureAwait(false);
            if (calibration.AbortDetail is not null)
            {
                return StopAndPersist(
                    id,
                    startedAt,
                    samples,
                    BaselineRunStatus.Aborted,
                    calibration.AbortDetail);
            }

            HeatProfile gpuProfile = calibration.Profile;
            abort = await RunAnchorHoldAsync(
                BaselinePhase.Gpu,
                startedAt,
                samples,
                "Holding the GPU heat. A rise near 15 °C only stops the search. Fans are not being changed.",
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return StopAndPersist(id, startedAt, samples, BaselineRunStatus.Aborted, abort);
            }

            _anchors.Add(new HeatAnchor(
                HeatAnchorKind.Gpu,
                gpuProfile,
                _lastAssessment,
                _lastReference,
                Rise(idleGpu, _lastReference)));

            HeatProfile mixed = gpuProfile with { CpuWorkers = HeatProfile.EverydayCpuWorkers };
            _workload.ApplyLow(mixed);
            _workload.Set(WorkloadLevel.Low);
            abort = await RunAnchorHoldAsync(
                BaselinePhase.Low,
                startedAt,
                samples,
                "Adding the safe CPU load and the frozen GPU heat together. Fans are not being changed.",
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return StopAndPersist(id, startedAt, samples, BaselineRunStatus.Aborted, abort);
            }

            _anchors.Add(new HeatAnchor(
                HeatAnchorKind.Mixed,
                mixed,
                _lastAssessment,
                _lastReference,
                Rise(idleGpu, _lastReference)));

            var referenceHolds = new List<HardwareSnapshot>(ReferenceStability.HoldCount);
            abort = await RunReferenceHoldsAsync(
                startedAt,
                samples,
                referenceHolds,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return StopAndPersist(id, startedAt, samples, BaselineRunStatus.Aborted, abort, referenceHolds);
            }

            abort = await RunPhaseAsync(
                BaselinePhase.Cooldown,
                WorkloadLevel.Idle,
                _schedule.Cooldown,
                startedAt,
                samples,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return StopAndPersist(id, startedAt, samples, BaselineRunStatus.Aborted, abort, referenceHolds);
            }

            _workload.Stop();
            return Persist(id, startedAt, samples, BaselineRunStatus.Completed, abortDetail: null, referenceHolds);
        }
        catch (OperationCanceledException)
        {
            return StopAndPersist(id, startedAt, samples, BaselineRunStatus.Cancelled, "Baseline cancelled.");
        }
        catch
        {
            _workload.Stop();
            throw;
        }
    }

    private async Task<string?> RunPhaseAsync(
        BaselinePhase phase,
        WorkloadLevel level,
        TimeSpan duration,
        DateTimeOffset startedAt,
        List<BaselineSample> samples,
        IProgress<BaselineProgress>? progress,
        CancellationToken cancellationToken)
    {
        _workload.Set(level);
        DateTimeOffset phaseEnd = _clock.GetUtcNow() + duration;
        bool sampled = false;
        bool heating = level != WorkloadLevel.Idle;

        while (!sampled || _clock.GetUtcNow() < phaseEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clock.GetUtcNow() - startedAt >= TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes))
            {
                return $"Baseline reached the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.";
            }

            HardwareSnapshot snapshot = _hardware.ReadSnapshot();
            _pumps ??= PumpWatch.Capture(snapshot);
            if (heating && _pumps.Check(snapshot) is string pumpSilent)
            {
                return pumpSilent;
            }

            samples.Add(new BaselineSample(_clock.GetUtcNow(), phase, snapshot));
            sampled = true;
            progress?.Report(new BaselineProgress(phase, snapshot, Describe(phase)));

            ThermalAbortReason? abort = heating
                ? SafetyLimits.EvaluateWhileHeating(snapshot, _workload, _limits)
                : SafetyLimits.Evaluate(snapshot, _limits);
            if (!heating && _workload.HasFault)
            {
                abort = ThermalAbortReason.GpuDeviceLost;
            }

            if (abort is not null)
            {
                return SafetyLimits.Describe(abort.Value, _limits);
            }

            if (_clock.GetUtcNow() >= phaseEnd)
            {
                break;
            }

            await _delay(_schedule.SamplePeriod, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<string?> RunReferenceHoldsAsync(
        DateTimeOffset startedAt,
        List<BaselineSample> samples,
        List<HardwareSnapshot> holds,
        IProgress<BaselineProgress>? progress,
        CancellationToken cancellationToken)
    {
        TimeSpan timeout = FanTestSchedule.Default.Timeout;
        for (int hold = 0; hold < ReferenceStability.HoldCount; hold++)
        {
            var window = new List<HardwareSnapshot>();
            DateTimeOffset deadline = _clock.GetUtcNow() + timeout;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_clock.GetUtcNow() - startedAt >= TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes))
                {
                    return $"Baseline reached the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.";
                }

                DateTimeOffset now = _clock.GetUtcNow();
                HardwareSnapshot snapshot = _hardware.ReadSnapshot() with { CapturedAt = now };
                _pumps ??= PumpWatch.Capture(snapshot);
                if (_pumps.Check(snapshot) is string pumpSilent)
                {
                    return pumpSilent;
                }
                samples.Add(new BaselineSample(now, BaselinePhase.Reference, snapshot));
                window.Add(snapshot);
                progress?.Report(new BaselineProgress(
                    BaselinePhase.Reference,
                    snapshot,
                    $"Checking how much this PC moves on its own. Same heat, fans still on BIOS. Hold {hold + 1} of {ReferenceStability.HoldCount}."));

                ThermalAbortReason? abort = SafetyLimits.EvaluateWhileHeating(snapshot, _workload, _limits);
                if (abort is not null)
                {
                    return SafetyLimits.Describe(abort.Value, _limits);
                }

                bool timedOut = now >= deadline;
                HoldAssessment assessment = HoldAssessor.Evaluate(window, timedOut);
                if (assessment == HoldAssessment.SettledMeasured)
                {
                    holds.Add(snapshot);
                    break;
                }

                if (timedOut)
                {
                    break;
                }

                await _delay(_schedule.SamplePeriod, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task<string?> RunAnchorHoldAsync(
        BaselinePhase phase,
        DateTimeOffset startedAt,
        List<BaselineSample> samples,
        string message,
        IProgress<BaselineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var window = new List<HardwareSnapshot>();
        DateTimeOffset deadline = _clock.GetUtcNow() + FanTestSchedule.Default.Timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_clock.GetUtcNow() - startedAt >= TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes))
            {
                return $"Baseline reached the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.";
            }

            DateTimeOffset now = _clock.GetUtcNow();
            HardwareSnapshot snapshot = _hardware.ReadSnapshot() with { CapturedAt = now };
            _pumps ??= PumpWatch.Capture(snapshot);
            if (_pumps.Check(snapshot) is string pumpSilent)
            {
                return pumpSilent;
            }

            samples.Add(new BaselineSample(now, phase, snapshot));
            window.Add(snapshot);
            progress?.Report(new BaselineProgress(phase, snapshot, message));

            ThermalAbortReason? abort = SafetyLimits.EvaluateWhileHeating(snapshot, _workload, _limits);
            if (abort is not null)
            {
                return SafetyLimits.Describe(abort.Value, _limits);
            }

            bool timedOut = now >= deadline;
            HoldAssessment assessment = HoldAssessor.Evaluate(window, timedOut);
            if (assessment == HoldAssessment.SettledMeasured || assessment != HoldAssessment.TransientModeled)
            {
                _lastAssessment = assessment;
                _lastReference = snapshot;
                return null;
            }

            await _delay(_schedule.SamplePeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private static double? Rise(double? idleGpuCelsius, HardwareSnapshot? snapshot)
    {
        if (idleGpuCelsius is not double idle || snapshot is null)
        {
            return null;
        }

        double? gpu = PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature);
        return gpu is double value ? value - idle : null;
    }

    private BaselineRun StopAndPersist(
        Guid id,
        DateTimeOffset startedAt,
        IReadOnlyList<BaselineSample> samples,
        BaselineRunStatus status,
        string abortDetail,
        IReadOnlyList<HardwareSnapshot>? referenceHolds = null)
    {
        _workload.Stop();
        return Persist(id, startedAt, samples, status, abortDetail, referenceHolds);
    }

    private BaselineRun Persist(
        Guid id,
        DateTimeOffset startedAt,
        IReadOnlyList<BaselineSample> samples,
        BaselineRunStatus status,
        string? abortDetail,
        IReadOnlyList<HardwareSnapshot>? referenceHolds = null)
    {
        List<BaselineMetric> metrics = [.. ThermalDynamics.Summarize(samples), .. ReferenceStability.ToMetrics(referenceHolds ?? [])];
        var run = new BaselineRun(
            id,
            startedAt,
            _clock.GetUtcNow(),
            status,
            abortDetail,
            ThermalDynamics.FirstAmbient(samples),
            _workload.GpuLoadAvailable,
            samples.ToArray(),
            metrics,
            _workload.LockedEveryday,
            _workload.LockedLow,
            Anchors: _anchors.ToArray());
        _store.Save(run);
        return run;
    }

    private static double? IdleGpu(IReadOnlyList<BaselineSample> samples)
    {
        for (int index = samples.Count - 1; index >= 0; index--)
        {
            if (samples[index].Phase != BaselinePhase.Idle)
            {
                continue;
            }

            return PreferredTemperature.Read(samples[index].Snapshot, SensorKind.GpuTemperature);
        }

        return null;
    }

    private static string Describe(BaselinePhase phase) =>
        phase switch
        {
            BaselinePhase.Idle => "Watching the PC at rest. Fans are not being changed.",
            BaselinePhase.Everyday =>
                "Adding CPU-only heat with the safe worker count. GPU work is off. Fans are not being changed.",
            BaselinePhase.Gpu =>
                "Holding GPU heat. About 15 °C of rise is the search aim, not proof a fan worked.",
            BaselinePhase.Low =>
                "Adding the safe CPU load and the frozen GPU heat together. Fans are not being changed.",
            BaselinePhase.High => "Adding heavier heat. Fans are not being changed. This can get close to the 90 °C limit.",
            BaselinePhase.Reference =>
                "Checking how much this PC moves on its own. Same heat, fans still on BIOS.",
            BaselinePhase.Cooldown => "Heat is off. Watching temperatures fall. Fans are not being changed.",
            _ => phase.ToString(),
        };
}
