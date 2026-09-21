using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class DummySessionTests
{
    [Fact]
    public void Run_stores_a_demo_snapshot_without_aborting()
    {
        var hardware = new FakeHardwareBackend();
        var store = new InMemorySessionStore();
        var session = new DummySession(hardware, store);

        DummySessionResult result = session.Run();

        Assert.Equal(DummySessionStatus.Observed, result.Status);
        Assert.True(result.Snapshot.IsDemoHardware);
        Assert.NotEmpty(result.Snapshot.FanGroups);
        Assert.NotEmpty(result.Snapshot.Sensors);
        Assert.Same(result, store.GetLatest());
        Assert.Single(store.List());
        Assert.Null(SafetyLimits.Evaluate(result.Snapshot));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(-1)]
    [InlineData(150)]
    public void ProposeDuty_rejects_out_of_range_values_and_leaves_fake_duty_unchanged(int percent)
    {
        var hardware = new FakeHardwareBackend();
        var store = new InMemorySessionStore();
        var session = new DummySession(hardware, store);
        int originalDuty = DutyOf(hardware, FakeHardwareBackend.FrontFanId);

        DummySessionResult result = session.ProposeDuty(FakeHardwareBackend.FrontFanId, percent);

        Assert.Equal(DummySessionStatus.DutyRejected, result.Status);
        Assert.Equal(originalDuty, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.Equal(originalDuty, DutyOf(result.Snapshot, FakeHardwareBackend.FrontFanId));
        Assert.Equal(result, store.GetLatest());
    }

    [Fact]
    public void ProposeDuty_applies_an_in_range_value_only_in_memory()
    {
        var hardware = new FakeHardwareBackend();
        var store = new InMemorySessionStore();
        var session = new DummySession(hardware, store);

        DummySessionResult result = session.ProposeDuty(FakeHardwareBackend.FrontFanId, 60);

        Assert.Equal(DummySessionStatus.DutyApplied, result.Status);
        Assert.Equal(60, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.Equal(60, DutyOf(result.Snapshot, FakeHardwareBackend.FrontFanId));
    }

    [Fact]
    public void Run_aborts_when_cpu_already_exceeds_the_ceiling()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 5);
        int originalDuty = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        var store = new InMemorySessionStore();
        var session = new DummySession(hardware, store);

        DummySessionResult result = session.Run();

        Assert.Equal(DummySessionStatus.Aborted, result.Status);
        Assert.Equal(ThermalAbortReason.CpuOverLimit, SafetyLimits.Evaluate(result.Snapshot));
        Assert.Equal(originalDuty, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.Contains("CPU", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ProposeDuty_does_not_change_duty_when_snapshot_is_already_over_abort()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, SafetyLimits.GpuAbortCelsius + 2);
        int originalDuty = DutyOf(hardware, FakeHardwareBackend.RearFanId);
        var store = new InMemorySessionStore();
        var session = new DummySession(hardware, store);

        DummySessionResult result = session.ProposeDuty(FakeHardwareBackend.RearFanId, 80);

        Assert.Equal(DummySessionStatus.Aborted, result.Status);
        Assert.Equal(originalDuty, DutyOf(hardware, FakeHardwareBackend.RearFanId));
        Assert.NotEqual(DummySessionStatus.Observed, result.Status);
        Assert.NotEqual(DummySessionStatus.DutyApplied, result.Status);
    }

    private static int DutyOf(IHardwareBackend hardware, string fanGroupId)
    {
        FanGroup group = hardware.FanGroups.Single(fan => fan.Id == fanGroupId);
        return group.DutyCyclePercent ?? throw new InvalidOperationException($"Fan group '{fanGroupId}' has no duty.");
    }

    private static int DutyOf(HardwareSnapshot snapshot, string fanGroupId)
    {
        FanGroup group = snapshot.FanGroups.Single(fan => fan.Id == fanGroupId);
        return group.DutyCyclePercent ?? throw new InvalidOperationException($"Fan group '{fanGroupId}' has no duty.");
    }
}
