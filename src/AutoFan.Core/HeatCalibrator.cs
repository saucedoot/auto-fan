namespace AutoFan.Core;

/// <summary>
/// Raises GPU work per frame until Core has risen enough from idle, then
/// freezes that profile. Does not write fans or add CPU threads.
/// </summary>
public static class HeatCalibrator
{
    public const double TargetRiseCelsius = 15;
    public const double CeilingMarginCelsius = 8;
    public const double MinimumHotSeparationCelsius = 5;
    public const int MaxPasses = 16;
    public const int MaxIterations = 256;
    public const int MaxIncreases = 6;

    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    public static HeatProfile IncreaseGpuWork(HeatProfile current)
    {
        if (current.GpuPasses < MaxPasses)
        {
            return current with { GpuPasses = Math.Min(MaxPasses, current.GpuPasses * 2) };
        }

        if (current.GpuIterations < MaxIterations)
        {
            return current with { GpuIterations = Math.Min(MaxIterations, current.GpuIterations * 2) };
        }

        return current;
    }

    public static bool CanIncreaseGpuWork(HeatProfile current) =>
        IncreaseGpuWork(current) != current;

    public static bool GpuRiseMet(double idleGpuCelsius, double gpuCelsius) =>
        gpuCelsius - idleGpuCelsius >= TargetRiseCelsius;

    public static bool AtCeilingMargin(double gpuCelsius, double abortGpuCelsius) =>
        gpuCelsius >= abortGpuCelsius - CeilingMarginCelsius;

    public static bool AtHotStop(
        double? cpuCelsius,
        double? gpuCelsius,
        ThermalAbortLimits limits) =>
        (cpuCelsius is double cpu && cpu >= limits.CpuCelsius - CeilingMarginCelsius)
        || (gpuCelsius is double gpu && gpu >= limits.GpuCelsius - CeilingMarginCelsius);

    public static bool SeparatesFromLow(
        double? hotCpuCelsius,
        double? lowCpuCelsius,
        double? hotGpuCelsius,
        double? lowGpuCelsius) =>
        SensorSeparates(hotCpuCelsius, lowCpuCelsius)
        || SensorSeparates(hotGpuCelsius, lowGpuCelsius);

    public static bool SensorSeparates(double? hotCelsius, double? lowCelsius) =>
        hotCelsius is double hot
        && lowCelsius is double low
        && hot - low >= MinimumHotSeparationCelsius;

    public static async Task<HeatCalibrationResult> RunAsync(
        IHardwareBackend hardware,
        IWorkloadActuator workload,
        double? idleGpuCelsius,
        ThermalAbortLimits limits,
        TimeProvider clock,
        DateTimeOffset startedAt,
        TimeSpan samplePeriod,
        Func<TimeSpan, CancellationToken, Task> delay,
        IProgress<BaselineProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(delay);

        if (!workload.GpuLoadAvailable)
        {
            workload.ApplyLow(HeatProfile.StartingLow);
            return new HeatCalibrationResult(HeatProfile.StartingLow, AbortDetail: null);
        }

        if (idleGpuCelsius is not double idleGpu)
        {
            return new HeatCalibrationResult(
                HeatProfile.StartingLow,
                SafetyLimits.Describe(ThermalAbortReason.TelemetryLost, limits));
        }

        HeatProfile profile = HeatProfile.StartingLow;
        workload.ApplyLow(profile);
        for (int step = 0; step <= MaxIncreases; step++)
        {
            HeatStepOutcome stepOutcome = await HoldStepAsync(
                hardware,
                workload,
                limits,
                clock,
                startedAt,
                samplePeriod,
                delay,
                progress,
                "Finding a heat level this PC can use. Fans are not being changed.",
                cancellationToken).ConfigureAwait(false);
            if (stepOutcome.AbortDetail is not null)
            {
                return new HeatCalibrationResult(profile, stepOutcome.AbortDetail);
            }

            double gpu = stepOutcome.GpuCelsius ?? 0;
            if (GpuRiseMet(idleGpu, gpu) || AtCeilingMargin(gpu, limits.GpuCelsius))
            {
                return new HeatCalibrationResult(profile, AbortDetail: null);
            }

            if (!CanIncreaseGpuWork(profile) || step == MaxIncreases)
            {
                return new HeatCalibrationResult(profile, AbortDetail: null);
            }

            profile = IncreaseGpuWork(profile);
            workload.ApplyLow(profile);
        }

        return new HeatCalibrationResult(profile, AbortDetail: null);
    }

    public static async Task<HeatCalibrationResult> RunHotAsync(
        IHardwareBackend hardware,
        IWorkloadActuator workload,
        HeatProfile startFromLow,
        ThermalAbortLimits limits,
        TimeProvider clock,
        DateTimeOffset startedAt,
        TimeSpan samplePeriod,
        Func<TimeSpan, CancellationToken, Task> delay,
        IProgress<BaselineProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(startFromLow);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(delay);

        HeatProfile profile = startFromLow with { CpuWorkers = HeatProfile.EverydayCpuWorkers };
        workload.ApplyHot(profile);
        if (!workload.GpuLoadAvailable)
        {
            return new HeatCalibrationResult(profile, AbortDetail: null);
        }

        for (int step = 0; step <= MaxIncreases; step++)
        {
            HeatStepOutcome stepOutcome = await HoldStepAsync(
                hardware,
                workload,
                limits,
                clock,
                startedAt,
                samplePeriod,
                delay,
                progress,
                "Finding a hotter heat this PC can use. Fans stay on BIOS.",
                cancellationToken).ConfigureAwait(false);
            if (stepOutcome.AbortDetail is not null)
            {
                return new HeatCalibrationResult(profile, stepOutcome.AbortDetail);
            }

            if (AtHotStop(stepOutcome.CpuCelsius, stepOutcome.GpuCelsius, limits))
            {
                return new HeatCalibrationResult(profile, AbortDetail: null);
            }

            if (!CanIncreaseGpuWork(profile) || step == MaxIncreases)
            {
                return new HeatCalibrationResult(profile, AbortDetail: null);
            }

            profile = IncreaseGpuWork(profile);
            workload.ApplyHot(profile);
        }

        return new HeatCalibrationResult(profile, AbortDetail: null);
    }

    private static async Task<HeatStepOutcome> HoldStepAsync(
        IHardwareBackend hardware,
        IWorkloadActuator workload,
        ThermalAbortLimits limits,
        TimeProvider clock,
        DateTimeOffset startedAt,
        TimeSpan samplePeriod,
        Func<TimeSpan, CancellationToken, Task> delay,
        IProgress<BaselineProgress>? progress,
        string message,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<HardwareSnapshot>();
        DateTimeOffset deadline = clock.GetUtcNow() + StepTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.GetUtcNow() - startedAt >= TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes))
            {
                return new HeatStepOutcome(
                    CpuCelsius: null,
                    GpuCelsius: null,
                    $"Baseline reached the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.");
            }

            HardwareSnapshot snapshot = hardware.ReadSnapshot();
            snapshots.Add(snapshot);
            progress?.Report(new BaselineProgress(BaselinePhase.Low, snapshot, message));

            ThermalAbortReason? abort = SafetyLimits.EvaluateWhileHeating(snapshot, workload, limits);
            if (abort is not null)
            {
                return new HeatStepOutcome(
                    CpuCelsius: null,
                    GpuCelsius: null,
                    SafetyLimits.Describe(abort.Value, limits));
            }

            DateTimeOffset now = clock.GetUtcNow();
            if (TemperatureSettle.RelevantTempsSettled(snapshots) || now >= deadline)
            {
                return new HeatStepOutcome(
                    PreferredTemperature.Read(snapshot, SensorKind.CpuTemperature),
                    PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature),
                    AbortDetail: null);
            }

            await delay(samplePeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private readonly record struct HeatStepOutcome(
        double? CpuCelsius,
        double? GpuCelsius,
        string? AbortDetail);
}

public sealed record HeatCalibrationResult(HeatProfile Profile, string? AbortDetail);
