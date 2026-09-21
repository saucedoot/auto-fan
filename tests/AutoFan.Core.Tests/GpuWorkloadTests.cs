using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class GpuWorkloadTests
{
    [Fact]
    public void Everyday_is_a_lighter_3d_burn_than_low_and_stays_vsync_capped()
    {
        HeatProfile everyday = GpuWorkload.HeatSettings(WorkloadLevel.Everyday);
        HeatProfile low = GpuWorkload.HeatSettings(WorkloadLevel.Low);
        HeatProfile high = GpuWorkload.HeatSettings(WorkloadLevel.High);

        Assert.Equal(1280, everyday.GpuWidth);
        Assert.Equal(720, everyday.GpuHeight);
        Assert.Equal(1, everyday.GpuPasses);
        Assert.Equal(16, everyday.GpuIterations);
        Assert.Equal(2560, low.GpuWidth);
        Assert.Equal(1440, low.GpuHeight);
        Assert.Equal(4, low.GpuPasses);
        Assert.Equal(64, low.GpuIterations);
        Assert.True(everyday.GpuWidth * everyday.GpuHeight < low.GpuWidth * low.GpuHeight);
        Assert.True(everyday.GpuWorkUnits < low.GpuWorkUnits);
        Assert.Equal(low, high);
        Assert.Equal(HeatProfile.Idle, GpuWorkload.HeatSettings(WorkloadLevel.Idle));
        Assert.Equal(HeatProfile.EverydayCpuWorkers, low.CpuWorkers);
        Assert.Equal(low.CpuWorkers, everyday.CpuWorkers);
        Assert.Equal(1u, GpuWorkload.PresentSyncInterval);
    }
}
