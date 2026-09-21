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

            abort = await RunPhaseAsync(
                BaselinePhase.Everyday,
                WorkloadLevel.Everyday,
                _schedule.Everyday,
                startedAt,
                samples,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return StopAndPersist(id, startedAt, samples, BaselineRunStatus.Aborted, abort);
            }

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
                cancellationToken).ConfigureAwait(false);
            if (calibration.AbortDetail is not null)
            {
                return StopAndPersist(
                    id,
                    startedAt,
                    samples,
                    BaselineRunStatus.Aborted,
                    calibration.AbortDetail);
            }

            abort = await RunPhaseAsync(
                BaselinePhase.Low,
                WorkloadLevel.Low,
                _schedule.Low,
                startedAt,
                samples,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (abort is not null)
            {
                return StopAndPersist(id, startedAt, samples, BaselineRunStatus.Aborted, abort);
            }

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

                HardwareSnapshot snapshot = _hardware.ReadSnapshot();
                DateTimeOffset now = _clock.GetUtcNow();
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

                if (TemperatureSettle.RelevantTempsSettled(window) || now >= deadline)
                {
                    holds.Add(snapshot);
                    break;
                }

                await _delay(_schedule.SamplePeriod, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
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
            _workload.LockedLow);
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
            BaselinePhase.Everyday => "Adding everyday heat, closer to normal use. Fans are not being changed.",
            BaselinePhase.Low => "Adding stronger heat so later fan tests can see a clear change. Fans are not being changed.",
            BaselinePhase.High => "Adding heavier heat. Fans are not being changed. This can get close to the 90 °C limit.",
            BaselinePhase.Reference =>
                "Checking how much this PC moves on its own. Same heat, fans still on BIOS.",
            BaselinePhase.Cooldown => "Heat is off. Watching temperatures fall. Fans are not being changed.",
            _ => phase.ToString(),
        };
}
