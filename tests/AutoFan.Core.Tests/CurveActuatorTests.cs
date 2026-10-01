using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class CurveActuatorTests
{
    [Fact]
    public void A_draft_may_not_take_control()
    {
        CoolingProfile draft = Profile(ProfileState.Draft);
        CoolingProfile validated = draft with { State = ProfileState.Validated };

        Assert.False(CurveActuator.MayTakeControl(draft));
        Assert.False(CurveActuator.MayTakeControl(null));
        Assert.True(CurveActuator.MayTakeControl(validated));
    }

    [Fact]
    public void The_higher_of_the_cpu_and_gpu_requests_is_used()
    {
        var memory = new ActuatorMemory();
        ActuatorDecision decision = CurveActuator.Next(
            MixedProfile(),
            memory,
            Reading(cpu: 50, gpu: 50, duty: 40, rpm: 800),
            gpuFault: false);

        ActuatorCommand command = Assert.Single(decision.Commands);
        Assert.Equal(55, command.DutyPercent);
        Assert.False(decision.Restore);
    }

    [Fact]
    public void A_small_temperature_drop_does_not_lower_the_fan()
    {
        var memory = new ActuatorMemory();
        CoolingProfile profile = FallingProfile();
        _ = CurveActuator.Next(profile, memory, Reading(cpu: 70, gpu: null, duty: 80, rpm: 900), false);
        ActuatorDecision held = CurveActuator.Next(
            profile,
            memory,
            Reading(cpu: 69.8, gpu: null, duty: 75, rpm: 900),
            false);

        Assert.Equal(75, Assert.Single(held.Commands).DutyPercent);
    }

    [Fact]
    public void A_real_temperature_drop_ramps_down_slowly()
    {
        var memory = new ActuatorMemory();
        CoolingProfile profile = FallingProfile();
        _ = CurveActuator.Next(profile, memory, Reading(cpu: 70, gpu: null, duty: 80, rpm: 900), false);
        ActuatorDecision dropped = CurveActuator.Next(
            profile,
            memory,
            Reading(cpu: 60, gpu: null, duty: 75, rpm: 900),
            false);

        Assert.Equal(70, Assert.Single(dropped.Commands).DutyPercent);
    }

    [Fact]
    public void A_power_rise_with_the_same_temperature_does_not_raise_the_fan()
    {
        var memory = new ActuatorMemory();
        CoolingProfile profile = FallingProfile();
        ActuatorDecision first = CurveActuator.Next(
            profile,
            memory,
            Reading(cpu: 40, gpu: null, duty: 80, rpm: 700, power: 40),
            false);
        ActuatorDecision second = CurveActuator.Next(
            profile,
            memory,
            Reading(cpu: 40, gpu: null, duty: first.Commands[0].DutyPercent, rpm: 700, power: 120),
            false);

        Assert.Equal(first.Commands[0].DutyPercent, second.Commands[0].DutyPercent);
    }

    [Fact]
    public void A_missing_temperature_restores()
    {
        ActuatorDecision decision = CurveActuator.Next(
            FallingProfile(),
            new ActuatorMemory(),
            Reading(cpu: null, gpu: null, duty: 40, rpm: 700),
            false);

        Assert.True(decision.Restore);
        Assert.Empty(decision.Commands);
    }

    [Fact]
    public void A_second_stall_invalidates_and_restores()
    {
        var memory = new ActuatorMemory();
        CoolingProfile profile = FallingProfile();
        var limits = new Dictionary<string, FanRunLimit>
        {
            [FakeHardwareBackend.FrontFanId] = new(30, 40),
        };
        _ = CurveActuator.Next(profile, memory, Reading(cpu: 40, gpu: null, duty: 20, rpm: 0), false, limits);
        ActuatorDecision spin = CurveActuator.Next(
            profile,
            memory,
            Reading(cpu: 40, gpu: null, duty: 20, rpm: 0),
            false,
            limits);
        ActuatorDecision failed = CurveActuator.Next(
            profile,
            memory,
            Reading(cpu: 40, gpu: null, duty: 100, rpm: 0),
            false,
            limits);

        Assert.Equal(40, Assert.Single(spin.Commands).DutyPercent);
        Assert.False(spin.Restore);
        Assert.True(failed.Restore);
        Assert.True(failed.Invalid);
    }

    [Fact]
    public void A_stopped_fan_is_not_commanded_below_its_start_duty()
    {
        var limits = new Dictionary<string, FanRunLimit>
        {
            [FakeHardwareBackend.FrontFanId] = new(20, 50),
        };
        ActuatorDecision decision = CurveActuator.Next(
            FallingProfile(),
            new ActuatorMemory(),
            Reading(cpu: 70, gpu: null, duty: 20, rpm: 0),
            false,
            limits);

        Assert.True(Assert.Single(decision.Commands).DutyPercent >= 50);
    }

    [Fact]
    public void A_draft_session_does_not_write_and_a_loss_restores()
    {
        var hardware = new FakeHardwareBackend();
        var session = new CurveSession(hardware, new FixedCompetingSoftwareScanner());
        CoolingProfile draft = Profile(ProfileState.Draft);

        Assert.False(session.TryStart(draft));
        Assert.False(hardware.HasActiveSoftwareControl);

        CoolingProfile validated = FallingProfile() with { State = ProfileState.Validated };
        Assert.True(session.TryStart(validated));
        _ = session.Tick(Reading(cpu: 40, gpu: null, duty: 40, rpm: 700));
        ActuatorDecision lost = session.Tick(Reading(cpu: null, gpu: null, duty: 40, rpm: 700));

        Assert.True(lost.Restore);
        Assert.False(session.IsRunning);
        Assert.False(hardware.HasActiveSoftwareControl);
        session.Dispose();
    }

    private static CoolingProfile Profile(ProfileState state) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            state,
            "Draft",
            [
                Point(CurveSensor.Cpu, 40, 40),
                Point(CurveSensor.Cpu, 70, 80),
            ]);

    private static CoolingProfile MixedProfile() =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            ProfileState.Draft,
            "Draft",
            [
                Point(CurveSensor.Cpu, 40, 40),
                Point(CurveSensor.Cpu, 70, 80),
                Point(CurveSensor.Gpu, 40, 70),
                Point(CurveSensor.Gpu, 80, 90),
            ]);

    private static CoolingProfile FallingProfile() =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            ProfileState.Draft,
            "Draft",
            [
                Point(CurveSensor.Cpu, 40, 80),
                Point(CurveSensor.Cpu, 70, 40),
            ]);

    private static CurvePoint Point(CurveSensor sensor, double temperature, int duty) =>
        new(FakeHardwareBackend.FrontFanId, sensor, temperature, duty, MetricEvidence.Measured, CurvePointOrigin.SettledHold);

    private static HardwareSnapshot Reading(double? cpu, double? gpu, int duty, double? rpm, double power = 40)
    {
        var sensors = new List<SensorReading>
        {
            new("power", "CPU Package", SensorKind.CpuPower, power, "W"),
        };
        if (cpu is double cpuValue)
        {
            sensors.Add(new SensorReading("cpu", "CPU Package", SensorKind.CpuTemperature, cpuValue, "°C"));
        }

        if (gpu is double gpuValue)
        {
            sensors.Add(new SensorReading("gpu", "GPU", SensorKind.GpuTemperature, gpuValue, "°C"));
        }

        return new HardwareSnapshot(
            DateTimeOffset.UnixEpoch,
            sensors,
            [new FanGroup(FakeHardwareBackend.FrontFanId, "Front", duty, rpm, "Demo", true)],
            IsDemoHardware: true);
    }
}
