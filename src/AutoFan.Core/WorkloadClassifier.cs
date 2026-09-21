namespace AutoFan.Core;

public static class WorkloadClassifier
{
    public const int BurstHoldTicks = 8;

    public static WorkloadState Classify(
        HardwareSnapshot snapshot,
        PowerReference reference,
        int elevatedTicks)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(reference);

        WorkloadPattern mix = Mix(snapshot, reference);
        WorkloadDuration duration = IsElevatedMix(mix) && elevatedTicks < BurstHoldTicks
            ? WorkloadDuration.Burst
            : WorkloadDuration.Sustained;
        return new WorkloadState(mix, duration);
    }

    public static WorkloadPattern Mix(HardwareSnapshot snapshot, PowerReference reference)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(reference);

        if (!reference.HasAnyWatts)
        {
            return WorkloadPattern.Unknown;
        }

        double? cpu = PreferredPower.Read(snapshot, SensorKind.CpuPower);
        double? gpu = PreferredPower.Read(snapshot, SensorKind.GpuPower);
        if (cpu is null && gpu is null)
        {
            return WorkloadPattern.Unknown;
        }

        bool cpuUp = IsElevated(cpu, reference.IdleCpuWatts, reference.EverydayCpuWatts, reference.TestCpuWatts);
        bool gpuUp = IsElevated(gpu, reference.IdleGpuWatts, reference.EverydayGpuWatts, reference.TestGpuWatts);
        return (cpuUp, gpuUp) switch
        {
            (false, false) => WorkloadPattern.Desktop,
            (false, true) => WorkloadPattern.Gaming,
            (true, false) => WorkloadPattern.Render,
            (true, true) => WorkloadPattern.Mixed,
        };
    }

    public static bool IsElevatedMix(WorkloadPattern pattern) =>
        pattern is WorkloadPattern.Gaming or WorkloadPattern.Render or WorkloadPattern.Mixed;

    private static bool IsElevated(double? live, double? idle, double? everyday, double? test)
    {
        if (live is not double watts)
        {
            return false;
        }

        if (everyday is null && idle is null)
        {
            return test is double testOnly && watts > testOnly;
        }

        double floor = everyday ?? idle ?? 0;
        if (test is double testWatts && testWatts > floor)
        {
            double threshold = floor + (0.35 * (testWatts - floor));
            return watts >= threshold;
        }

        double margin = Math.Max(10, floor * 0.35);
        return watts >= floor + margin;
    }
}
