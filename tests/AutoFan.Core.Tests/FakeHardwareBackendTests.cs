using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class FakeHardwareBackendTests
{
    [Theory]
    [InlineData(101)]
    [InlineData(-1)]
    public void TrySetDuty_rejects_illegal_percents_even_if_core_is_bypassed(int percent)
    {
        var hardware = new FakeHardwareBackend();
        int? originalDuty = hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent;

        DutySetResult result = hardware.TrySetDuty(FakeHardwareBackend.FrontFanId, percent);

        Assert.False(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(
            originalDuty,
            hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent);
    }

    [Fact]
    public void TrySetDuty_updates_only_in_memory_rpm_for_a_legal_percent()
    {
        var hardware = new FakeHardwareBackend();

        DutySetResult result = hardware.TrySetDuty(FakeHardwareBackend.FrontFanId, 80);

        Assert.True(result.Accepted);
        FanGroup front = hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId);
        Assert.Equal(80, front.DutyCyclePercent);
        Assert.Equal(1120, front.Rpm);
        Assert.True(hardware.IsDemo);
        Assert.True(hardware.HasActiveSoftwareControl);
        Assert.Contains("not this machine", hardware.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RestoreDefaults_returns_duties_to_the_original_values()
    {
        var hardware = new FakeHardwareBackend();
        int original = hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent
            ?? throw new InvalidOperationException("Front fan has no duty.");

        Assert.True(hardware.TrySetDuty(FakeHardwareBackend.FrontFanId, 80).Accepted);
        hardware.RestoreDefaults();

        Assert.Equal(original, hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent);
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void TrySetDuty_rejects_pumps_and_amd_gpu_headers()
    {
        var hardware = new FakeHardwareBackend();

        DutySetResult pump = hardware.TrySetDuty(FakeHardwareBackend.PumpId, 30);
        DutySetResult amd = hardware.TrySetDuty(FakeHardwareBackend.AmdGpuFanId, 30);

        Assert.False(pump.Accepted);
        Assert.Equal(FakeHardwareBackend.PumpRejectedMessage, pump.Error);
        Assert.False(amd.Accepted);
        Assert.Equal(FakeHardwareBackend.GpuRejectedMessage, amd.Error);
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void TrySetDuty_writes_nvidia_gpu_and_restore_returns_it()
    {
        var hardware = new FakeHardwareBackend();
        int original = hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.GpuFanId).DutyCyclePercent
            ?? throw new InvalidOperationException("GPU fan has no duty.");

        DutySetResult write = hardware.TrySetDuty(FakeHardwareBackend.GpuFanId, 80);

        Assert.True(write.Accepted);
        Assert.Equal(80, hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.GpuFanId).DutyCyclePercent);
        Assert.True(hardware.HasActiveSoftwareControl);

        hardware.RestoreDefaults();

        Assert.Equal(original, hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.GpuFanId).DutyCyclePercent);
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void TrySetDuty_applies_an_nvidia_write_to_coupled_gpu_fans()
    {
        var hardware = new FakeHardwareBackend();
        hardware.AddFan(
            "gpu-fan-2",
            "GPU Fan #2",
            duty: 40,
            maxRpm: 1900,
            isGpu: true,
            controllerName: "NVIDIA GeForce RTX 4070");
        int amdOriginal = hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.AmdGpuFanId).DutyCyclePercent
            ?? throw new InvalidOperationException("AMD GPU fan has no duty.");

        Assert.True(hardware.TrySetDuty(FakeHardwareBackend.GpuFanId, 80).Accepted);

        Assert.Equal(80, hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.GpuFanId).DutyCyclePercent);
        Assert.Equal(80, hardware.FanGroups.Single(fan => fan.Id == "gpu-fan-2").DutyCyclePercent);
        Assert.Equal(amdOriginal, hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.AmdGpuFanId).DutyCyclePercent);
    }

    [Fact]
    public void TrySetDuty_aborts_and_restores_when_a_ceiling_is_already_crossed()
    {
        var hardware = new FakeHardwareBackend();
        int originalFront = hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent
            ?? throw new InvalidOperationException("Front fan has no duty.");
        Assert.True(hardware.TrySetDuty(FakeHardwareBackend.FrontFanId, 80).Accepted);
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 4);

        DutySetResult result = hardware.TrySetDuty(FakeHardwareBackend.RearFanId, 90);

        Assert.False(result.Accepted);
        Assert.Contains("CPU", result.Error, StringComparison.Ordinal);
        Assert.Equal(
            originalFront,
            hardware.FanGroups.Single(fan => fan.Id == FakeHardwareBackend.FrontFanId).DutyCyclePercent);
        Assert.False(hardware.HasActiveSoftwareControl);
    }
}
