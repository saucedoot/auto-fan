using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class HeatCalibratorTests
{
    [Fact]
    public void IncreaseGpuWork_doubles_passes_then_iterations_and_never_adds_cpu_workers()
    {
        HeatProfile start = HeatProfile.StartingLow;
        Assert.Equal(1, start.GpuPasses);
        Assert.Equal(64, start.GpuIterations);
        Assert.Equal(HeatProfile.EverydayCpuWorkers, start.CpuWorkers);

        HeatProfile next = HeatCalibrator.IncreaseGpuWork(start);
        Assert.Equal(2, next.GpuPasses);
        Assert.Equal(64, next.GpuIterations);
        Assert.Equal(start.CpuWorkers, next.CpuWorkers);

        HeatProfile atPassCap = start with { GpuPasses = HeatCalibrator.MaxPasses };
        HeatProfile afterPassCap = HeatCalibrator.IncreaseGpuWork(atPassCap);
        Assert.Equal(HeatCalibrator.MaxPasses, afterPassCap.GpuPasses);
        Assert.Equal(128, afterPassCap.GpuIterations);

        HeatProfile capped = start with
        {
            GpuPasses = HeatCalibrator.MaxPasses,
            GpuIterations = HeatCalibrator.MaxIterations,
        };
        Assert.Equal(capped, HeatCalibrator.IncreaseGpuWork(capped));
        Assert.False(HeatCalibrator.CanIncreaseGpuWork(capped));
    }

    [Fact]
    public async Task Run_locks_when_gpu_rise_from_idle_reaches_15_and_does_not_write_fans_or_use_high()
    {
        var hardware = new FakeHardwareBackend();
        int frontDuty = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        var workload = new FakeWorkloadActuator
        {
            OnApplyLow = profile =>
            {
                if (profile.GpuPasses >= 2)
                {
                    hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 58);
                }
            },
        };
        var clock = new ManualTimeProvider();

        HeatCalibrationResult result = await HeatCalibrator.RunAsync(
            hardware,
            workload,
            idleGpuCelsius: 42,
            ThermalAbortLimits.Floor,
            clock,
            clock.GetUtcNow(),
            TimeSpan.FromSeconds(1),
            Delay(clock),
            progress: null,
            CancellationToken.None);

        Assert.Null(result.AbortDetail);
        Assert.Equal(2, result.Profile.GpuPasses);
        Assert.Equal(64, result.Profile.GpuIterations);
        Assert.Equal(HeatProfile.EverydayCpuWorkers, result.Profile.CpuWorkers);
        Assert.Equal(result.Profile, workload.LockedLow);
        Assert.DoesNotContain(WorkloadLevel.High, workload.History);
        Assert.All(workload.AppliedLow, profile => Assert.Equal(HeatProfile.EverydayCpuWorkers, profile.CpuWorkers));
        Assert.Equal(frontDuty, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public async Task Run_stops_increasing_near_the_gpu_ceiling()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 76);
        var workload = new FakeWorkloadActuator();
        var clock = new ManualTimeProvider();

        HeatCalibrationResult result = await HeatCalibrator.RunAsync(
            hardware,
            workload,
            idleGpuCelsius: 42,
            ThermalAbortLimits.Floor,
            clock,
            clock.GetUtcNow(),
            TimeSpan.FromSeconds(1),
            Delay(clock),
            progress: null,
            CancellationToken.None);

        Assert.Null(result.AbortDetail);
        Assert.Equal(HeatProfile.StartingLow, result.Profile);
        Assert.Equal(HeatProfile.StartingLow, workload.LockedLow);
        Assert.Single(workload.AppliedLow);
    }

    [Fact]
    public async Task Run_locks_strongest_safe_profile_when_gpu_stays_too_cool()
    {
        var hardware = new FakeHardwareBackend();
        var workload = new FakeWorkloadActuator();
        var clock = new ManualTimeProvider();

        HeatCalibrationResult result = await HeatCalibrator.RunAsync(
            hardware,
            workload,
            idleGpuCelsius: 42,
            ThermalAbortLimits.Floor,
            clock,
            clock.GetUtcNow(),
            TimeSpan.FromSeconds(1),
            Delay(clock),
            progress: null,
            CancellationToken.None);

        Assert.Null(result.AbortDetail);
        Assert.Equal(HeatCalibrator.MaxPasses, result.Profile.GpuPasses);
        Assert.Equal(HeatCalibrator.MaxIterations, result.Profile.GpuIterations);
        Assert.Equal(HeatProfile.EverydayCpuWorkers, result.Profile.CpuWorkers);
        Assert.True(workload.AppliedLow.Count > 1);
        Assert.DoesNotContain(WorkloadLevel.High, workload.History);
    }

    [Fact]
    public void SeparatesFromLow_skips_when_neither_sensor_is_five_degrees_hotter()
    {
        Assert.False(HeatCalibrator.SeparatesFromLow(49, 45, 44, 42));
        Assert.False(HeatCalibrator.SeparatesFromLow(49.9, 45, 46.9, 42));
        Assert.False(HeatCalibrator.SeparatesFromLow(null, 45, null, 42));
        Assert.False(HeatCalibrator.SeparatesFromLow(60, null, 70, null));
    }

    [Fact]
    public void SeparatesFromLow_keeps_when_cpu_or_gpu_is_five_degrees_hotter()
    {
        Assert.True(HeatCalibrator.SeparatesFromLow(50, 45, 42, 42));
        Assert.True(HeatCalibrator.SeparatesFromLow(45, 45, 47, 42));
        Assert.True(HeatCalibrator.SeparatesFromLow(51, 45, 48, 42));
    }

    [Fact]
    public void AtHotStop_is_eight_under_abort_not_a_new_floor()
    {
        Assert.True(HeatCalibrator.AtHotStop(82, 40, ThermalAbortLimits.Floor));
        Assert.True(HeatCalibrator.AtHotStop(40, 75, ThermalAbortLimits.Floor));
        Assert.False(HeatCalibrator.AtHotStop(81, 74, ThermalAbortLimits.Floor));
        Assert.Equal(90, SafetyLimits.CpuAbortCelsius);
        Assert.Equal(83, SafetyLimits.GpuAbortCelsius);
        Assert.Equal(82, SafetyLimits.CpuAbortCelsius - HeatCalibrator.CeilingMarginCelsius);
        Assert.Equal(75, SafetyLimits.GpuAbortCelsius - HeatCalibrator.CeilingMarginCelsius);
    }

    [Fact]
    public async Task RunHot_starts_from_low_raises_gpu_work_and_never_uses_high_cpu()
    {
        HeatProfile storedLow = new(HeatProfile.EverydayCpuWorkers, 2560, 1440, 1, 48);
        var hardware = new FakeHardwareBackend();
        int frontDuty = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        var workload = new FakeWorkloadActuator();
        var clock = new ManualTimeProvider();

        HeatCalibrationResult result = await HeatCalibrator.RunHotAsync(
            hardware,
            workload,
            storedLow,
            ThermalAbortLimits.Floor,
            clock,
            clock.GetUtcNow(),
            TimeSpan.FromSeconds(1),
            Delay(clock),
            progress: null,
            CancellationToken.None);

        Assert.Null(result.AbortDetail);
        Assert.Equal(HeatProfile.EverydayCpuWorkers, result.Profile.CpuWorkers);
        Assert.True(result.Profile.GpuWorkUnits > storedLow.GpuWorkUnits);
        Assert.Equal(result.Profile, workload.LockedHot);
        Assert.Null(workload.LockedLow);
        Assert.DoesNotContain(WorkloadLevel.High, workload.History);
        Assert.All(workload.AppliedHot, profile => Assert.Equal(HeatProfile.EverydayCpuWorkers, profile.CpuWorkers));
        Assert.Empty(workload.AppliedLow);
        Assert.Equal(frontDuty, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public async Task RunHot_stops_increasing_about_eight_under_cpu_or_gpu_abort()
    {
        HeatProfile storedLow = new(HeatProfile.EverydayCpuWorkers, 2560, 1440, 1, 48);
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 82);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 75);
        var workload = new FakeWorkloadActuator();
        var clock = new ManualTimeProvider();

        HeatCalibrationResult result = await HeatCalibrator.RunHotAsync(
            hardware,
            workload,
            storedLow,
            ThermalAbortLimits.Floor,
            clock,
            clock.GetUtcNow(),
            TimeSpan.FromSeconds(1),
            Delay(clock),
            progress: null,
            CancellationToken.None);

        Assert.Null(result.AbortDetail);
        Assert.Equal(storedLow with { CpuWorkers = HeatProfile.EverydayCpuWorkers }, result.Profile);
        Assert.Single(workload.AppliedHot);
        Assert.DoesNotContain(WorkloadLevel.High, workload.History);
    }

    [Fact]
    public async Task Run_aborts_when_the_gpu_device_is_lost()
    {
        var hardware = new FakeHardwareBackend();
        var workload = new FakeWorkloadActuator();
        workload.OnApplyLow = _ => workload.HasFault = true;
        var clock = new ManualTimeProvider();

        HeatCalibrationResult result = await HeatCalibrator.RunAsync(
            hardware,
            workload,
            idleGpuCelsius: 42,
            ThermalAbortLimits.Floor,
            clock,
            clock.GetUtcNow(),
            TimeSpan.FromSeconds(1),
            Delay(clock),
            progress: null,
            CancellationToken.None);

        Assert.Equal(SafetyLimits.Describe(ThermalAbortReason.GpuDeviceLost), result.AbortDetail);
    }

    private static Func<TimeSpan, CancellationToken, Task> Delay(ManualTimeProvider clock) =>
        (span, token) =>
        {
            token.ThrowIfCancellationRequested();
            clock.Advance(span);
            return Task.CompletedTask;
        };

    private static int DutyOf(IHardwareBackend hardware, string fanGroupId)
    {
        FanGroup group = hardware.FanGroups.Single(fan => fan.Id == fanGroupId);
        return group.DutyCyclePercent ?? throw new InvalidOperationException($"Fan group '{fanGroupId}' has no duty.");
    }
}
