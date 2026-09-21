using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class PolicySessionTests
{
    [Fact]
    public void Start_writes_duties_and_dispose_restores()
    {
        var hardware = new FakeHardwareBackend();
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        CoolingPolicy policy = FrontPolicy(quiet: 35, cool: 60, applied: 50);

        using (var session = new PolicySession(hardware, EmptyCompetingSoftwareScanner.Instance, policy))
        {
            DutySetResult started = session.Start();
            Assert.True(started.Accepted);
            Assert.True(session.IsActive);
            Assert.Equal(50, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
            Assert.True(hardware.HasActiveSoftwareControl);
        }

        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void Tick_raises_toward_cool_when_a_target_is_crossed()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 81);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 70);
        CoolingPolicy policy = FrontPolicy(quiet: 35, cool: 60, applied: 35);
        using var session = new PolicySession(hardware, EmptyCompetingSoftwareScanner.Instance, policy);

        Assert.True(session.Start().Accepted);
        session.Tick();

        Assert.False(session.IsAborted);
        Assert.Equal(40, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.Equal(40, session.CurrentDuty(FakeHardwareBackend.FrontFanId));
    }

    [Fact]
    public void Tick_lowers_toward_quiet_when_temps_are_well_under()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 60);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 55);
        CoolingPolicy policy = FrontPolicy(quiet: 35, cool: 60, applied: 60);
        using var session = new PolicySession(hardware, EmptyCompetingSoftwareScanner.Instance, policy);

        Assert.True(session.Start().Accepted);
        session.Tick();

        Assert.Equal(55, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
    }

    [Fact]
    public void Tick_aborts_and_restores_when_temperature_crosses_the_ceiling()
    {
        var hardware = new FakeHardwareBackend();
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 78);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 72);
        CoolingPolicy policy = FrontPolicy(quiet: 35, cool: 60, applied: 50);
        using var session = new PolicySession(hardware, EmptyCompetingSoftwareScanner.Instance, policy);

        Assert.True(session.Start().Accepted);
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, SafetyLimits.CpuAbortCelsius + 1);
        session.Tick();

        Assert.True(session.IsAborted);
        Assert.False(session.IsActive);
        Assert.Contains("CPU", session.AbortDetail, StringComparison.Ordinal);
        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.False(hardware.HasActiveSoftwareControl);
    }

    [Fact]
    public void Stop_restores_without_abort()
    {
        var hardware = new FakeHardwareBackend();
        int original = DutyOf(hardware, FakeHardwareBackend.FrontFanId);
        CoolingPolicy policy = FrontPolicy(quiet: 35, cool: 60, applied: 48);
        using var session = new PolicySession(hardware, EmptyCompetingSoftwareScanner.Instance, policy);

        Assert.True(session.Start().Accepted);
        session.Stop();

        Assert.False(session.IsAborted);
        Assert.False(session.IsActive);
        Assert.Equal(original, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
    }

    [Fact]
    public void Tick_raises_toward_cool_when_live_heat_exceeds_the_test_load()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 70);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 55);
        CoolingPolicy policy = new(
            [
                new GroupPolicy(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    QuietDutyPercent: 35,
                    CoolDutyPercent: 60,
                    DutyPercent: 35,
                    AppliedRpm: null),
            ],
            QuietCool: 0,
            CoolingPreferences.DefaultCpuTargetCelsius,
            CoolingPreferences.DefaultGpuTargetCelsius,
            "test",
            TestCpuCelsius: 62,
            TestGpuCelsius: 50);
        using var session = new PolicySession(hardware, EmptyCompetingSoftwareScanner.Instance, policy);

        Assert.True(session.Start().Accepted);
        session.Tick();

        Assert.Equal(40, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
    }

    [Fact]
    public void Tick_raises_from_sustained_gpu_power_before_temps_need_it()
    {
        var hardware = new FakeHardwareBackend();
        ThermalModel model = WorkloadPolicyFixtures.GpuFrontModel(hardware);
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 55);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 50);
        hardware.OverrideReading(FakeHardwareBackend.GpuPowerId, 70);
        CoolingPolicy policy = FrontPolicy(quiet: 35, cool: 60, applied: 35);
        using var session = new PolicySession(
            hardware,
            EmptyCompetingSoftwareScanner.Instance,
            policy,
            model: model);

        Assert.True(session.Start().Accepted);
        for (int tick = 0; tick < WorkloadClassifier.BurstHoldTicks - 1; tick++)
        {
            session.Tick();
        }

        Assert.Equal(35, DutyOf(hardware, FakeHardwareBackend.FrontFanId));

        session.Tick();

        Assert.Equal(40, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.Contains("GPU power rose", session.Status().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tick_does_not_raise_from_gpu_power_when_control_is_temp_only()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 55);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 50);
        hardware.OverrideReading(FakeHardwareBackend.GpuPowerId, 70);
        CoolingPolicy policy = FrontPolicy(quiet: 35, cool: 60, applied: 35);
        using var session = new PolicySession(hardware, EmptyCompetingSoftwareScanner.Instance, policy);

        Assert.True(session.Start().Accepted);
        for (int tick = 0; tick < WorkloadClassifier.BurstHoldTicks; tick++)
        {
            session.Tick();
        }

        Assert.Equal(35, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.Contains("Holding the chosen fan speeds", session.Status().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tick_lowers_to_quiet_on_desktop_idle()
    {
        var hardware = new FakeHardwareBackend();
        ThermalModel model = WorkloadPolicyFixtures.GpuFrontModel(hardware);
        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 50);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 45);
        CoolingPolicy policy = FrontPolicy(quiet: 35, cool: 60, applied: 50);
        using var session = new PolicySession(
            hardware,
            EmptyCompetingSoftwareScanner.Instance,
            policy,
            model: model);

        Assert.True(session.Start().Accepted);
        session.Tick();

        Assert.Equal(45, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.Contains("Desktop", session.Status().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Low_only_model_keeps_the_recommended_point_until_live_heat_needs_cool()
    {
        var hardware = new FakeHardwareBackend();
        ThermalModel model = WorkloadPolicyFixtures.GpuFrontModel(hardware);
        CoolingPolicy? recommended = PolicyOptimizer.Recommend(
            model,
            DiminishingReturnsAnalyzer.Analyze([]),
            CoolingPreferences.Default);
        Assert.NotNull(recommended);
        Assert.NotEqual(ModelConfidence.High, model.Confidence);
        int stored = recommended.Groups[0].DutyPercent;

        hardware.OverrideTemperature(FakeHardwareBackend.CpuSensorId, 50);
        hardware.OverrideTemperature(FakeHardwareBackend.GpuSensorId, 45);
        using var session = new PolicySession(
            hardware,
            EmptyCompetingSoftwareScanner.Instance,
            recommended,
            model: model);
        Assert.True(session.Start().Accepted);
        Assert.Equal(stored, session.CurrentDuty(FakeHardwareBackend.FrontFanId));

        hardware.OverrideReading(FakeHardwareBackend.GpuPowerId, 70);
        for (int tick = 0; tick < WorkloadClassifier.BurstHoldTicks; tick++)
        {
            session.Tick();
        }

        Assert.True(session.CurrentDuty(FakeHardwareBackend.FrontFanId) > stored);
    }

    [Fact]
    public void AddAirflow_raises_duty_a_step_after_a_confirmation_miss()
    {
        var hardware = new FakeHardwareBackend();
        CoolingPolicy policy = FrontPolicy(quiet: 35, cool: 60, applied: 50);
        using var session = new PolicySession(hardware, EmptyCompetingSoftwareScanner.Instance, policy);

        Assert.True(session.Start().Accepted);
        DutySetResult extra = session.AddAirflow();

        Assert.True(extra.Accepted);
        Assert.Equal(55, DutyOf(hardware, FakeHardwareBackend.FrontFanId));
        Assert.Equal(55, session.CurrentDuty(FakeHardwareBackend.FrontFanId));
    }

    private static CoolingPolicy FrontPolicy(int quiet, int cool, int applied) =>
        new(
            [
                new GroupPolicy(
                    FakeHardwareBackend.FrontFanId,
                    "Front intake",
                    quiet,
                    cool,
                    applied,
                    AppliedRpm: null),
            ],
            QuietCool: 0.5,
            CoolingPreferences.DefaultCpuTargetCelsius,
            CoolingPreferences.DefaultGpuTargetCelsius,
            "test");

    private static int DutyOf(IHardwareBackend hardware, string fanGroupId)
    {
        FanGroup group = hardware.FanGroups.Single(fan => fan.Id == fanGroupId);
        return group.DutyCyclePercent ?? throw new InvalidOperationException($"Fan group '{fanGroupId}' has no duty.");
    }
}
