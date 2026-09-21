using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

public sealed class WorkloadGroupRolesTests
{
    [Fact]
    public void Gaming_raises_gpu_groups_and_render_raises_cpu_groups()
    {
        InfluenceEntry[] influence =
        [
            new(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Gpu,
                3.0,
                InfluenceEffect.High,
                MetricEvidence.Measured,
                35,
                60,
                490,
                840),
            new(
                FakeHardwareBackend.RearFanId,
                "Rear exhaust",
                InfluenceTarget.Cpu,
                2.4,
                InfluenceEffect.Medium,
                MetricEvidence.Measured,
                30,
                55,
                360,
                660),
        ];

        Assert.Equal(WorkloadGroupRole.Gpu, WorkloadGroupRoles.RoleOf(influence, FakeHardwareBackend.FrontFanId));
        Assert.Equal(WorkloadGroupRole.Cpu, WorkloadGroupRoles.RoleOf(influence, FakeHardwareBackend.RearFanId));
        Assert.True(WorkloadGroupRoles.ShouldRaiseFromPower(WorkloadGroupRole.Gpu, WorkloadPattern.Gaming));
        Assert.False(WorkloadGroupRoles.ShouldRaiseFromPower(WorkloadGroupRole.Cpu, WorkloadPattern.Gaming));
        Assert.True(WorkloadGroupRoles.ShouldRaiseFromPower(WorkloadGroupRole.Cpu, WorkloadPattern.Render));
        Assert.True(WorkloadGroupRoles.ShouldRaiseFromPower(WorkloadGroupRole.None, WorkloadPattern.Mixed));
    }
}
