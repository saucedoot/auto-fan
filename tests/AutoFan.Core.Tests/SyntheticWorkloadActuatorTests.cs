using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class SyntheticWorkloadActuatorTests
{
    [Fact]
    public void Set_low_without_a_frozen_profile_does_not_fall_back_to_default_low()
    {
        using var actuator = new SyntheticWorkloadActuator();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => actuator.Set(WorkloadLevel.Low));

        Assert.Equal(HeatProfile.MissingLampDetail, error.Message);
        Assert.Null(actuator.LockedLow);
        Assert.NotEqual(HeatProfile.DefaultLow, actuator.LockedLow);
    }

    [Fact]
    public void Set_low_reuses_the_applied_profile_not_default_low()
    {
        using var actuator = new SyntheticWorkloadActuator();
        HeatProfile stored = new(HeatProfile.EverydayCpuWorkers, 2560, 1440, 1, 48);
        Assert.NotEqual(HeatProfile.DefaultLow, stored);

        actuator.ApplyLow(stored);
        actuator.Stop();
        actuator.Set(WorkloadLevel.Low);

        Assert.Equal(stored, actuator.LockedLow);
        Assert.NotEqual(HeatProfile.DefaultLow, actuator.LockedLow);
    }

    [Fact]
    public void Set_everyday_remembers_the_everyday_profile()
    {
        using var actuator = new SyntheticWorkloadActuator();

        actuator.Set(WorkloadLevel.Everyday);

        Assert.Equal(HeatProfile.Everyday, actuator.LockedEveryday);
        Assert.Null(actuator.LockedLow);
    }
}
