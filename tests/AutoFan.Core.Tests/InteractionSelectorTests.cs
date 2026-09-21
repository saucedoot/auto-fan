using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class InteractionSelectorTests
{
    [Fact]
    public void App_md_slight_plus_slight_pair_is_eligible()
    {
        var hardware = new FakeHardwareBackend();
        IReadOnlyList<(FanGroup First, FanGroup Second)> pairs = InteractionSelector.Select(
            hardware.FanGroups,
            [
                Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Gpu, 1.5, InfluenceEffect.Low),
                Measured(FakeHardwareBackend.TopFanId, "Top exhaust", InfluenceTarget.Gpu, 0.7, InfluenceEffect.Low),
            ]);

        (FanGroup first, FanGroup second) = Assert.Single(pairs);
        Assert.True(first.Id == FakeHardwareBackend.FrontFanId || first.Id == FakeHardwareBackend.TopFanId);
        Assert.True(second.Id == FakeHardwareBackend.FrontFanId || second.Id == FakeHardwareBackend.TopFanId);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Motherboard_order_is_not_used_when_influence_is_missing()
    {
        var hardware = new FakeHardwareBackend();

        Assert.Empty(InteractionSelector.Select(hardware.FanGroups));
        Assert.Empty(InteractionSelector.Select(hardware.FanGroups, []));
    }

    [Fact]
    public void None_effect_alone_is_not_paired()
    {
        var hardware = new FakeHardwareBackend();
        IReadOnlyList<(FanGroup First, FanGroup Second)> pairs = InteractionSelector.Select(
            hardware.FanGroups,
            [
                Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Gpu, 0.2, InfluenceEffect.None),
                Measured(FakeHardwareBackend.TopFanId, "Top exhaust", InfluenceTarget.Gpu, 0.1, InfluenceEffect.None),
            ]);

        Assert.Empty(pairs);
    }

    [Fact]
    public void Coupled_nvidia_gpu_fans_are_not_paired_with_each_other()
    {
        var hardware = new FakeHardwareBackend();
        hardware.AddFan(
            "gpu-fan-2",
            "GPU Fan #2",
            duty: 40,
            maxRpm: 1900,
            isGpu: true,
            controllerName: "NVIDIA GeForce RTX 4070");
        IReadOnlyList<(FanGroup First, FanGroup Second)> pairs = InteractionSelector.Select(
            hardware.FanGroups,
            [
                Measured(FakeHardwareBackend.GpuFanId, "GPU Fan", InfluenceTarget.Gpu, 6.5, InfluenceEffect.VeryHigh),
                Measured("gpu-fan-2", "GPU Fan #2", InfluenceTarget.Gpu, 6.4, InfluenceEffect.VeryHigh),
                Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Gpu, 1.5, InfluenceEffect.Low),
            ]);

        Assert.NotEmpty(pairs);
        Assert.DoesNotContain(
            pairs,
            pair => FanWriteCandidates.AreCoupled(pair.First, pair.Second));
        Assert.Contains(
            pairs,
            pair => pair.First.Id == FakeHardwareBackend.FrontFanId
                || pair.Second.Id == FakeHardwareBackend.FrontFanId);
    }

    private static InfluenceEntry Measured(
        string id,
        string name,
        InfluenceTarget target,
        double delta,
        InfluenceEffect effect) =>
        new(id, name, target, delta, effect, MetricEvidence.Measured, 35, 60, 490, 840);
}
