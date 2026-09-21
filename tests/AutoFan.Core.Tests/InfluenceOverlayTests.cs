using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class InfluenceOverlayTests
{
    [Fact]
    public void Overlay_keeps_an_untested_group_old_influence_row()
    {
        var previous = new FanTestRun(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Gpu, 4.2, InfluenceEffect.High),
                Measured(FakeHardwareBackend.RearFanId, "Rear exhaust", InfluenceTarget.Cpu, 4.5, InfluenceEffect.High),
            ],
            []);
        var targeted = new FanTestRun(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                Measured(FakeHardwareBackend.FrontFanId, "Front intake", InfluenceTarget.Gpu, 3.1, InfluenceEffect.High),
            ],
            []);

        FanTestRun merged = InfluenceOverlay.Apply(previous, targeted);

        Assert.NotEqual(targeted.Id, merged.Id);
        Assert.Contains(
            merged.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.DeltaCelsius == 3.1);
        Assert.Contains(
            merged.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.RearFanId
                && entry.DeltaCelsius == 4.5);
        Assert.DoesNotContain(
            merged.Influence,
            entry => entry.FanGroupId == FakeHardwareBackend.FrontFanId
                && entry.DeltaCelsius == 4.2);
    }

    private static InfluenceEntry Measured(
        string id,
        string name,
        InfluenceTarget target,
        double delta,
        InfluenceEffect effect) =>
        new(id, name, target, delta, effect, MetricEvidence.Measured, 35, 60, 490, 840);
}
