using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class FanWriteCandidatesTests
{
    [Fact]
    public void IsWritableTestFan_includes_nvidia_and_motherboard_and_skips_amd()
    {
        var motherboard = new FanGroup("/lpc/fan1", "CPU Fan", 40, 900, "Nuvoton", true);
        var nvidia = new FanGroup("/gpu/0/fan/0", "GPU Fan", 40, 800, "NVIDIA GeForce RTX 4070", true);
        var amd = new FanGroup("/gpu/1/fan/0", "GPU Fan", 40, 700, "AMD Radeon RX 7800 XT", true);
        var intel = new FanGroup("/gpu/2/fan/0", "GPU Fan", 0, 0, "Intel UHD Graphics 770", true);
        var pump = new FanGroup("/pump", "AIO Pump", 50, 3000, "Nuvoton", true, FanGroupKind.Pump);

        Assert.True(FanWriteCandidates.IsWritableMotherboardFan(motherboard));
        Assert.True(FanWriteCandidates.IsWritableNvidiaGpuFan(nvidia));
        Assert.True(FanWriteCandidates.IsWritableTestFan(nvidia));
        Assert.False(FanWriteCandidates.IsWritableNvidiaGpuFan(amd));
        Assert.False(FanWriteCandidates.IsWritableTestFan(amd));
        Assert.False(FanWriteCandidates.IsWritableTestFan(intel));
        Assert.False(FanWriteCandidates.IsWritableTestFan(pump));
        Assert.True(FanWriteCandidates.LooksAmdOrIntelGpu(amd.ControllerName, amd.Name));
    }

    [Fact]
    public void Nvidia_fans_on_the_same_gpu_share_a_coupled_set()
    {
        var gpu1 = new FanGroup("/gpu-nvidia/0/control/1", "GPU Fan #1", 40, 800, "NVIDIA GeForce RTX 5070 Ti", true);
        var gpu2 = new FanGroup("/gpu-nvidia/0/control/2", "GPU Fan #2", 40, 810, "NVIDIA GeForce RTX 5070 Ti", true);
        var gpuOther = new FanGroup("/gpu-nvidia/1/control/1", "GPU Fan #1", 40, 700, "NVIDIA GeForce RTX 4090", true);
        var cpu = new FanGroup("/lpc/nct6687dr/0/control/0", "CPU Fan", 40, 900, "Nuvoton NCT6687D-R", true);

        Assert.Equal(FanWriteCandidates.CoupledSetKey(gpu1), FanWriteCandidates.CoupledSetKey(gpu2));
        Assert.NotEqual(FanWriteCandidates.CoupledSetKey(gpu1), FanWriteCandidates.CoupledSetKey(gpuOther));
        Assert.Null(FanWriteCandidates.CoupledSetKey(cpu));
        Assert.True(FanWriteCandidates.AreCoupled(gpu1, gpu2));
        Assert.False(FanWriteCandidates.AreCoupled(gpu1, gpuOther));
        Assert.False(FanWriteCandidates.AreCoupled(gpu1, cpu));

        IReadOnlyList<FanGroup> one = FanWriteCandidates.TakeOnePerCoupledSet([gpu1, gpu2, cpu]);
        Assert.Equal(2, one.Count);
        Assert.Contains(one, group => group.Id == gpu1.Id);
        Assert.Contains(one, group => group.Id == cpu.Id);
        Assert.DoesNotContain(one, group => group.Id == gpu2.Id);

        FanGroup dead = gpu1 with { Rpm = 0 };
        IReadOnlyList<FanGroup> picked = FanWriteCandidates.TakeOnePerCoupledSet([dead, gpu2]);
        Assert.Equal(gpu2.Id, Assert.Single(picked).Id);
    }
}
