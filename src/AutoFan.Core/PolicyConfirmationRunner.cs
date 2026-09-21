namespace AutoFan.Core;

public static class PolicyConfirmationRunner
{
    public static async Task<PolicyConfirmation> ConfirmAsync(
        IHardwareBackend hardware,
        PolicySession? session,
        double? expectedCpu,
        double? expectedGpu,
        TimeProvider? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hardware);

        TimeProvider time = clock ?? TimeProvider.System;
        Func<TimeSpan, CancellationToken, Task> wait = delay ?? ((span, token) => Task.Delay(span, token));
        IReadOnlyList<HardwareSnapshot> settled = await CollectSettledAsync(
            hardware,
            session,
            time,
            wait,
            cancellationToken).ConfigureAwait(false);
        double? cpu = PreferredTemperature.Read(settled[^1], SensorKind.CpuTemperature);
        double? gpu = PreferredTemperature.Read(settled[^1], SensorKind.GpuTemperature);
        bool missed = PolicyConfirmer.Missed(cpu, expectedCpu, gpu, expectedGpu);
        bool added = false;
        if (missed && session is { IsActive: true, IsAborted: false })
        {
            DutySetResult extra = session.AddAirflow();
            added = extra.Accepted;
            if (added)
            {
                settled = await CollectSettledAsync(
                    hardware,
                    session,
                    time,
                    wait,
                    cancellationToken).ConfigureAwait(false);
                cpu = PreferredTemperature.Read(settled[^1], SensorKind.CpuTemperature);
                gpu = PreferredTemperature.Read(settled[^1], SensorKind.GpuTemperature);
            }
        }

        return PolicyConfirmer.Complete(
            cpu,
            gpu,
            expectedCpu,
            expectedGpu,
            added,
            time.GetUtcNow(),
            ThermalDynamics.ReadAmbient(settled[^1]));
    }

    private static async Task<IReadOnlyList<HardwareSnapshot>> CollectSettledAsync(
        IHardwareBackend hardware,
        PolicySession? session,
        TimeProvider clock,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<HardwareSnapshot>();
        DateTimeOffset deadline = clock.GetUtcNow() + FanTestSchedule.Default.Timeout;
        while (clock.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (session is { IsAborted: true })
            {
                break;
            }

            snapshots.Add(hardware.ReadSnapshot());
            if (TemperatureSettle.RelevantTempsSettled(snapshots))
            {
                break;
            }

            await delay(FanTestSchedule.Default.SamplePeriod, cancellationToken).ConfigureAwait(false);
        }

        if (snapshots.Count == 0)
        {
            snapshots.Add(hardware.ReadSnapshot());
        }

        return snapshots;
    }
}
