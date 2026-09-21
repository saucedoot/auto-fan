using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class PowerReferenceTests
{
    [Fact]
    public void FromBaseline_averages_idle_everyday_and_low_watts()
    {
        var hardware = new FakeHardwareBackend();
        BaselineRun baseline = WorkloadPolicyFixtures.PowerBaseline(hardware);

        PowerReference reference = PowerReference.FromBaseline(baseline);

        Assert.Equal(35, reference.IdleCpuWatts);
        Assert.Equal(20, reference.IdleGpuWatts);
        Assert.Equal(50, reference.EverydayCpuWatts);
        Assert.Equal(30, reference.EverydayGpuWatts);
        Assert.Equal(90, reference.TestCpuWatts);
        Assert.Equal(80, reference.TestGpuWatts);
    }

    [Fact]
    public void Missing_baseline_is_empty()
    {
        PowerReference reference = PowerReference.FromBaseline(null);

        Assert.Same(PowerReference.Empty, reference);
        Assert.False(reference.HasAnyWatts);
    }
}
