using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class SafeFanSessionTests
{
    [Fact]
    public void Dispose_restores_duty_after_a_successful_write()
    {
        var hardware = new FakeHardwareBackend();
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);

        using (var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance))
        {
            DutySetResult result = session.TrySetDuty(FakeHardwareBackend.FrontFanId, 80);
            Assert.True(result.Accepted);
            Assert.Equal(80, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
            Assert.True(hardware.HasActiveSoftwareControl);
        }

        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void Restore_returns_control_without_disposing()
    {
        var hardware = new FakeHardwareBackend();
        int original = DutyOf(hardware, FakeHardwareBackend.RearFanId);
        using var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance);

        Assert.True(session.TrySetDuty(FakeHardwareBackend.RearFanId, 70).Accepted);
        session.Restore();

        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.RearFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
        Assert.False(session.IsAborted);
    }

    [Fact]
    public void TrySetDuty_rejects_competing_software_and_does_not_take_control()
    {
        var hardware = new FakeHardwareBackend();
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        var scanner = new FixedCompetingSoftwareScanner("FanControl");
        using var session = new SafeFanSession(hardware, scanner);

        DutySetResult result = session.TrySetDuty(FakeHardwareBackend.FrontFanId, 60);

        Assert.False(result.Accepted);
        Assert.Contains("FanControl", result.Error, StringComparison.Ordinal);
        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
        Assert.False(session.IsAborted);
    }

    [Fact]
    public void TrySetDuty_aborts_and_restores_when_temperature_is_already_over_the_ceiling()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 1);
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        using var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance);

        DutySetResult result = session.TrySetDuty(FakeHardwareBackend.FrontFanId, 90);

        Assert.False(result.Accepted);
        Assert.True(session.IsAborted);
        Assert.Contains("CPU", result.Error, StringComparison.Ordinal);
        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void CheckLimits_restores_after_a_write_when_temperature_crosses_the_ceiling()
    {
        var hardware = new FakeHardwareBackend();
        int original = DutyOf(hardware, FakeHardwareBackend.TopFanId);
        using var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance);

        Assert.True(session.TrySetDuty(FakeHardwareBackend.TopFanId, 55).Accepted);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, SafetyLimits.GpuAbortCelsius + 2);
        session.CheckLimits();

        Assert.True(session.IsAborted);
        Assert.Contains("GPU", session.AbortDetail, StringComparison.Ordinal);
        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.TopFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void CheckLimits_restores_when_the_duration_ceiling_is_reached()
    {
        var hardware = new FakeHardwareBackend();
        var clock = new ManualTimeProvider();
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        using var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance, clock);

        Assert.True(session.TrySetDuty(FakeHardwareBackend.FrontFanId, 65).Accepted);
        clock.Advance(TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes));
        session.CheckLimits();

        Assert.True(session.IsAborted);
        Assert.Contains("minute", session.AbortDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void CheckLimits_does_not_abort_before_the_duration_ceiling()
    {
        var hardware = new FakeHardwareBackend();
        var clock = new ManualTimeProvider();
        using var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance, clock);

        Assert.True(session.TrySetDuty(FakeHardwareBackend.FrontFanId, 65).Accepted);
        clock.Advance(TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes) - TimeSpan.FromSeconds(1));
        session.CheckLimits();

        Assert.False(session.IsAborted);
        Assert.Equal(65, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.True(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void Policy_session_does_not_abort_after_the_experiment_duration()
    {
        var hardware = new FakeHardwareBackend();
        var clock = new ManualTimeProvider();
        using var session = new SafeFanSession(
            hardware,
            EmptyCompetingSoftwareScanner.Instance,
            clock,
            FanSessionKind.Policy);

        Assert.True(session.TrySetDuty(FakeHardwareBackend.FrontFanId, 65).Accepted);
        clock.Advance(TimeSpan.FromMinutes(SafetyLimits.MaxExperimentDurationMinutes + 1));
        session.CheckLimits();

        Assert.False(session.IsAborted);
        Assert.Equal(65, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.True(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void CheckLimits_aborts_on_a_sudden_temperature_climb()
    {
        var hardware = new FakeHardwareBackend();
        var clock = new ManualTimeProvider();
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        using var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance, clock);

        Assert.True(session.TrySetDuty(FakeHardwareBackend.FrontFanId, 65).Accepted);
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 80);
        session.CheckLimits();
        clock.Advance(TimeSpan.FromSeconds(1));
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 84);
        session.CheckLimits();
        clock.Advance(TimeSpan.FromSeconds(1));
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 88);
        session.CheckLimits();
        clock.Advance(TimeSpan.FromSeconds(1));
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 89);
        session.CheckLimits();

        Assert.True(session.IsAborted);
        Assert.Contains("too quickly", session.AbortDetail, StringComparison.Ordinal);
        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void TrySetDuty_rejects_out_of_range_duty_without_taking_control()
    {
        var hardware = new FakeHardwareBackend();
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        using var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance);

        DutySetResult result = session.TrySetDuty(FakeHardwareBackend.FrontFanId, 140);

        Assert.False(result.Accepted);
        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void TrySetDuty_rejects_pump_groups()
    {
        var hardware = new FakeHardwareBackend();
        using var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance);

        DutySetResult result = session.TrySetDuty(FakeHardwareBackend.PumpId, 40);

        Assert.False(result.Accepted);
        Assert.Equal(FakeHardwareBackend.PumpRejectedMessage, result.Error);
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void Aborted_session_rejects_later_writes()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 1);
        using var session = new SafeFanSession(hardware, EmptyCompetingSoftwareScanner.Instance);

        Assert.False(session.TrySetDuty(FakeHardwareBackend.FrontFanId, 50).Accepted);
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 40);
        DutySetResult later = session.TrySetDuty(FakeHardwareBackend.FrontFanId, 50);

        Assert.False(later.Accepted);
        Assert.True(session.IsAborted);
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void CheckLimits_restores_if_competing_software_appears_after_a_write()
    {
        var hardware = new FakeHardwareBackend();
        var scanner = new FixedCompetingSoftwareScanner();
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        using var session = new SafeFanSession(hardware, scanner);

        Assert.True(session.TrySetDuty(FakeHardwareBackend.FrontFanId, 75).Accepted);
        scanner.Set("iCUE");
        session.CheckLimits();

        Assert.True(session.IsAborted);
        Assert.Contains("iCUE", session.AbortDetail, StringComparison.Ordinal);
        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
    }

    private static int DutyOf(IHardwareBackend hardware, string fanGroupId)
    {
        FanGroup group = hardware.FanGroups.Single(fan => fan.Id == fanGroupId);
        return group.DutyCyclePercent ?? throw new InvalidOperationException($"Fan group '{fanGroupId}' has no duty.");
    }
}
