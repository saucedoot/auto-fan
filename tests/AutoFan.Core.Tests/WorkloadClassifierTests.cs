using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class WorkloadClassifierTests
{
    [Fact]
    public void Idle_watts_are_desktop()
    {
        var hardware = new FakeHardwareBackend();
        PowerReference reference = WorkloadPolicyFixtures.Reference();

        WorkloadState state = WorkloadClassifier.Classify(hardware.ReadSnapshot(), reference, elevatedTicks: 0);

        Assert.Equal(WorkloadPattern.Desktop, state.Pattern);
        Assert.Equal(WorkloadDuration.Sustained, state.Duration);
        Assert.False(state.IsPowerLead);
    }

    [Fact]
    public void Gpu_only_jump_is_gaming()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideReading(FakeHardwareBackend.GpuPowerId, 70);
        PowerReference reference = WorkloadPolicyFixtures.Reference();

        WorkloadState burst = WorkloadClassifier.Classify(hardware.ReadSnapshot(), reference, elevatedTicks: 1);
        WorkloadState held = WorkloadClassifier.Classify(
            hardware.ReadSnapshot(),
            reference,
            WorkloadClassifier.BurstHoldTicks);

        Assert.Equal(WorkloadPattern.Gaming, burst.Pattern);
        Assert.Equal(WorkloadDuration.Burst, burst.Duration);
        Assert.False(burst.IsPowerLead);
        Assert.Equal(WorkloadPattern.Gaming, held.Pattern);
        Assert.Equal(WorkloadDuration.Sustained, held.Duration);
        Assert.True(held.IsPowerLead);
    }

    [Fact]
    public void Cpu_only_jump_is_render()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideReading(FakeHardwareBackend.CpuPowerId, 85);
        PowerReference reference = WorkloadPolicyFixtures.Reference();

        WorkloadState state = WorkloadClassifier.Classify(
            hardware.ReadSnapshot(),
            reference,
            WorkloadClassifier.BurstHoldTicks);

        Assert.Equal(WorkloadPattern.Render, state.Pattern);
        Assert.True(state.IsPowerLead);
    }

    [Fact]
    public void Both_cpu_and_gpu_up_is_mixed()
    {
        var hardware = new FakeHardwareBackend();
        hardware.OverrideReading(FakeHardwareBackend.CpuPowerId, 85);
        hardware.OverrideReading(FakeHardwareBackend.GpuPowerId, 70);
        PowerReference reference = WorkloadPolicyFixtures.Reference();

        WorkloadState state = WorkloadClassifier.Classify(
            hardware.ReadSnapshot(),
            reference,
            WorkloadClassifier.BurstHoldTicks);

        Assert.Equal(WorkloadPattern.Mixed, state.Pattern);
        Assert.True(state.IsPowerLead);
    }

    [Fact]
    public void Missing_reference_is_unknown_even_when_live_watts_exist()
    {
        var hardware = new FakeHardwareBackend();

        WorkloadState state = WorkloadClassifier.Classify(hardware.ReadSnapshot(), PowerReference.Empty, 0);

        Assert.Equal(WorkloadPattern.Unknown, state.Pattern);
        Assert.False(state.IsPowerLead);
    }

    [Fact]
    public void Missing_live_watts_are_unknown()
    {
        var snapshot = new HardwareSnapshot(DateTimeOffset.UtcNow, [], [], IsDemoHardware: true);

        WorkloadState state = WorkloadClassifier.Classify(snapshot, WorkloadPolicyFixtures.Reference(), 0);

        Assert.Equal(WorkloadPattern.Unknown, state.Pattern);
        Assert.False(WorkloadClassifier.IsElevatedMix(state.Pattern));
    }
}
