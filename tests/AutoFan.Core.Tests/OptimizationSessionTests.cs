using AutoFan.Core;

namespace AutoFan.Core.Tests;

public sealed class OptimizationSessionTests
{
    [Fact]
    public async Task A_settled_hold_passes_and_restores()
    {
        var plant = new SimulatedThermalPlant();
        plant.RestAt(70, 40);
        OptimizationSession session = Session(plant, TimeSpan.FromSeconds(40));

        OptimizationSessionResult result = await session.RunAsync(SimulatedThermalPlant.CpuFanId, 70);

        Assert.Equal(OptimizationSessionStatus.Passed, result.Status);
        Assert.Equal(HoldAssessment.SettledMeasured, result.Assessment);
        Assert.True(result.SoftwareControlReleased);
        Assert.False(plant.HasActiveSoftwareControl);
        Assert.Equal([70], result.SoftwareDuties);
    }

    [Fact]
    public async Task A_fought_duty_is_not_a_pass()
    {
        var plant = new SimulatedThermalPlant { FightWrites = true };
        plant.RestAt(70, 40);
        OptimizationSession session = Session(plant, TimeSpan.FromSeconds(20));

        OptimizationSessionResult result = await session.RunAsync(SimulatedThermalPlant.CpuFanId, 70);

        Assert.NotEqual(OptimizationSessionStatus.Passed, result.Status);
        Assert.Contains("did not stick", result.Detail, StringComparison.Ordinal);
        Assert.True(result.SoftwareControlReleased);
    }

    [Fact]
    public async Task Slow_creep_is_not_settled_in_five_seconds_and_does_not_pass()
    {
        var plant = new SimulatedThermalPlant { CreepCelsiusPerSecond = 0.15 };
        plant.RestAt(70, 40);
        IReadOnlyList<HardwareSnapshot> trace = Record(plant, seconds: 6);

        TraceReplayResult replay = TraceReplayer.Replay(
            trace,
            dutyCommanded: true,
            SimulatedThermalPlant.CpuFanId);

        Assert.NotEqual(HoldAssessment.SettledMeasured, replay.AtFiveSeconds);
        Assert.False(replay.CalledCooling);

        OptimizationSessionResult result = await Session(plant, TimeSpan.FromSeconds(30))
            .RunAsync(SimulatedThermalPlant.CpuFanId, 70);

        Assert.NotEqual(OptimizationSessionStatus.Passed, result.Status);
        Assert.NotEqual(HoldAssessment.SettledMeasured, result.Assessment);
        Assert.True(result.SoftwareControlReleased);
    }

    [Fact]
    public async Task Power_drift_is_not_called_cooling()
    {
        var plant = new SimulatedThermalPlant
        {
            PowerDriftWattsPerSecond = -3,
            TemperatureDriftCelsiusPerSecond = -0.01,
        };
        plant.RestAt(70, 40);
        IReadOnlyList<HardwareSnapshot> trace = Record(plant, seconds: 20);
        TraceReplayResult replay = TraceReplayer.Replay(
            trace,
            dutyCommanded: true,
            SimulatedThermalPlant.CpuFanId,
            timedOut: true);

        Assert.Equal(HoldAssessment.PowerUnstable, replay.AtEnd);
        Assert.False(replay.CalledCooling);

        var sessionPlant = new SimulatedThermalPlant
        {
            PowerDriftWattsPerSecond = -3,
            TemperatureDriftCelsiusPerSecond = -0.01,
        };
        sessionPlant.RestAt(70, 40);
        OptimizationSessionResult result = await Session(sessionPlant, TimeSpan.FromSeconds(40))
            .RunAsync(SimulatedThermalPlant.CpuFanId, 70);

        Assert.Equal(OptimizationSessionStatus.Failed, result.Status);
        Assert.Equal(HoldAssessment.PowerUnstable, result.Assessment);
        Assert.True(result.SoftwareControlReleased);
    }

    [Fact]
    public async Task A_missing_cpu_temperature_does_not_quiet_the_fans()
    {
        var plant = new SimulatedThermalPlant { DropCpuTemperatureAfter = TimeSpan.FromSeconds(2) };
        plant.RestAt(70, 40);
        OptimizationSession session = Session(plant, TimeSpan.FromSeconds(20));

        OptimizationSessionResult result = await session.RunAsync(SimulatedThermalPlant.CpuFanId, 70);

        Assert.Equal(OptimizationSessionStatus.Failed, result.Status);
        Assert.Equal(HoldAssessment.TelemetryLost, result.Assessment);
        Assert.All(result.SoftwareDuties, duty => Assert.True(duty >= 70));
        Assert.DoesNotContain(plant.Commands, command => command.SoftwareControl && command.Percent < 70);
        Assert.True(result.SoftwareControlReleased);
        Assert.False(plant.HasActiveSoftwareControl);
    }

    [Fact]
    public async Task Cancel_releases_software_control()
    {
        var plant = new SimulatedThermalPlant();
        plant.RestAt(70, 40);
        var clock = new ManualTimeProvider();
        var session = new OptimizationSession(
            plant,
            clock,
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                throw new OperationCanceledException();
            },
            timeout: TimeSpan.FromSeconds(20));

        OptimizationSessionResult result = await session.RunAsync(SimulatedThermalPlant.CpuFanId, 70);

        Assert.Equal(OptimizationSessionStatus.Cancelled, result.Status);
        Assert.True(result.SoftwareControlReleased);
        Assert.False(plant.HasActiveSoftwareControl);
    }

    [Fact]
    public async Task An_overheat_fails_and_releases_software_control()
    {
        var plant = new SimulatedThermalPlant { OverheatCpuAfter = TimeSpan.FromSeconds(2) };
        plant.RestAt(70, 40);
        OptimizationSession session = Session(plant, TimeSpan.FromSeconds(20));

        OptimizationSessionResult result = await session.RunAsync(SimulatedThermalPlant.CpuFanId, 70);

        Assert.Equal(OptimizationSessionStatus.Failed, result.Status);
        Assert.Contains("CPU", result.Detail, StringComparison.Ordinal);
        Assert.True(result.SoftwareControlReleased);
        Assert.False(plant.HasActiveSoftwareControl);
    }

    [Fact]
    public async Task A_stalled_fan_is_not_a_pass()
    {
        var plant = new SimulatedThermalPlant { StallCpuFan = true };
        plant.RestAt(70, 40);
        OptimizationSession session = Session(plant, TimeSpan.FromSeconds(20));

        OptimizationSessionResult result = await session.RunAsync(SimulatedThermalPlant.CpuFanId, 70);

        Assert.Equal(HoldAssessment.FanStalled, result.Assessment);
        Assert.NotEqual(OptimizationSessionStatus.Passed, result.Status);
        Assert.True(result.SoftwareControlReleased);
    }

    [Fact]
    public void The_other_fan_moves_cpu_temperature()
    {
        var alone = new SimulatedThermalPlant { NoiseCelsius = 0 };
        alone.RestAt(40, 40);
        alone.Advance(TimeSpan.FromSeconds(60));
        double cpuAlone = PreferredTemperature.Read(alone.ReadSnapshot(), SensorKind.CpuTemperature)!.Value;

        var coupled = new SimulatedThermalPlant { NoiseCelsius = 0 };
        coupled.RestAt(40, 40);
        Assert.True(coupled.TrySetDuty(SimulatedThermalPlant.GpuFanId, 100).Accepted);
        coupled.Advance(TimeSpan.FromSeconds(60));
        double cpuCoupled = PreferredTemperature.Read(coupled.ReadSnapshot(), SensorKind.CpuTemperature)!.Value;

        Assert.True(cpuCoupled < cpuAlone - 1);
    }

    private static OptimizationSession Session(SimulatedThermalPlant plant, TimeSpan timeout)
    {
        var clock = new ManualTimeProvider();
        return new OptimizationSession(
            plant,
            clock,
            (span, token) =>
            {
                token.ThrowIfCancellationRequested();
                clock.Advance(span);
                plant.Advance(span);
                return Task.CompletedTask;
            },
            samplePeriod: TimeSpan.FromSeconds(1),
            timeout: timeout);
    }

    private static IReadOnlyList<HardwareSnapshot> Record(SimulatedThermalPlant plant, int seconds)
    {
        var trace = new List<HardwareSnapshot>();
        for (int second = 0; second < seconds; second++)
        {
            trace.Add(plant.ReadSnapshot());
            plant.Advance(TimeSpan.FromSeconds(1));
        }

        return trace;
    }
}
