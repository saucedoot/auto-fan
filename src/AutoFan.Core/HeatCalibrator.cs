namespace AutoFan.Core;

/// <summary>
/// Raises GPU work per frame until Core has risen enough from idle, then
/// freezes that profile. Does not write fans or add CPU threads.
/// </summary>
public static class HeatCalibrator
{
    public const double TargetRiseCelsius = 15;
    public const double CeilingMarginCelsius = 8;
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
                cancellationToken).ConfigureAwait(false);
            if (stepOutcome.AbortDetail is not null)
            {
                return new HeatCalibrationResult(profile, stepOutcome.AbortDetail);
            }

            double gpu = stepOutcome.GpuCelsius;
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

    private static async Task<HeatStepOutcome> HoldStepAsync(
        IHardwareBackend hardware,
        IWorkloadActuator workload,
        ThermalAbortLimits limits,
        TimeProvider clock,
        DateTimeOffset startedAt,
        TimeSpan samplePeriod,
        Func<TimeSpan, CancellationToken, Task> delay,
        IProgress<BaselineProgress>? progress,
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
                    0,
                    $"Baseline reached the {SafetyLimits.MaxExperimentDurationMinutes}-minute abort limit.");
            }

            HardwareSnapshot snapshot = hardware.ReadSnapshot();
            snapshots.Add(snapshot);
            progress?.Report(new BaselineProgress(
                BaselinePhase.Low,
                snapshot,
                "Finding a heat level this PC can use. Fans are not being changed."));

            ThermalAbortReason? abort = SafetyLimits.EvaluateWhileHeating(snapshot, workload, limits);
            if (abort is not null)
            {
                return new HeatStepOutcome(0, SafetyLimits.Describe(abort.Value, limits));
            }

            DateTimeOffset now = clock.GetUtcNow();
            if (TemperatureSettle.RelevantTempsSettled(snapshots) || now >= deadline)
            {
                double gpu = PreferredTemperature.Read(snapshot, SensorKind.GpuTemperature)
                    ?? 0;
                return new HeatStepOutcome(gpu, AbortDetail: null);
            }

            await delay(samplePeriod, cancellationToken).ConfigureAwait(false);
        }
    }

    private readonly record struct HeatStepOutcome(double GpuCelsius, string? AbortDetail);
}

public sealed record HeatCalibrationResult(HeatProfile Profile, string? AbortDetail);
