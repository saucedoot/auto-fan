using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class CpuWorkloadTests
{
    [Fact]
    public void Low_uses_the_same_cpu_workers_as_everyday_and_high_uses_all_cores()
    {
        int everyday = CpuWorkload.WorkerCount(WorkloadLevel.Everyday);
        int low = CpuWorkload.WorkerCount(WorkloadLevel.Low);
        int high = CpuWorkload.WorkerCount(WorkloadLevel.High);

        Assert.Equal(Math.Max(1, Environment.ProcessorCount / 4), everyday);
        Assert.Equal(HeatProfile.EverydayCpuWorkers, everyday);
        Assert.Equal(everyday, low);
        Assert.Equal(Math.Max(1, Environment.ProcessorCount), high);
        Assert.True(low <= high);
        Assert.Equal(0, CpuWorkload.WorkerCount(WorkloadLevel.Idle));
    }
}
