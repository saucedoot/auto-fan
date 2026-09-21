using AutoFan.Core;
using AutoFan.Hardware;

namespace AutoFan.Core.Tests;

internal static class WorkloadPolicyFixtures
{
    public static readonly HardwareIdentity Identity = new(
        "ASUS ROG STRIX Z790-E",
        "Intel Core i7-13700K",
        "NVIDIA GeForce RTX 4070",
        "ASUS ROG STRIX Z790-E");

    public static PowerReference Reference() =>
        new(
            IdleCpuWatts: 35,
            IdleGpuWatts: 20,
            EverydayCpuWatts: 50,
            EverydayGpuWatts: 30,
            TestCpuWatts: 90,
            TestGpuWatts: 80);

    public static BaselineRun PowerBaseline(FakeHardwareBackend hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);

        DateTimeOffset at = DateTimeOffset.UtcNow;
        hardware.OverrideReading(FakeHardwareBackend.CpuPowerId, 35);
        hardware.OverrideReading(FakeHardwareBackend.GpuPowerId, 20);
        HardwareSnapshot idle = hardware.ReadSnapshot();
        hardware.OverrideReading(FakeHardwareBackend.CpuPowerId, 50);
        hardware.OverrideReading(FakeHardwareBackend.GpuPowerId, 30);
        HardwareSnapshot everyday = hardware.ReadSnapshot();
        hardware.OverrideReading(FakeHardwareBackend.CpuPowerId, 90);
        hardware.OverrideReading(FakeHardwareBackend.GpuPowerId, 80);
        HardwareSnapshot low = hardware.ReadSnapshot();
        hardware.OverrideReading(FakeHardwareBackend.CpuPowerId, 35);
        hardware.OverrideReading(FakeHardwareBackend.GpuPowerId, 20);

        return new BaselineRun(
            Guid.NewGuid(),
            at,
            at,
            BaselineRunStatus.Completed,
            AbortDetail: null,
            AmbientCelsius: null,
            GpuLoadAvailable: true,
            [
                new BaselineSample(at, BaselinePhase.Idle, idle),
                new BaselineSample(at, BaselinePhase.Everyday, everyday),
                new BaselineSample(at, BaselinePhase.Low, low),
            ],
            []);
    }

    public static ThermalModel GpuFrontModel(FakeHardwareBackend hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);

        var run = new FanTestRun(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            [],
            [
                new InfluenceEntry(
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
            ],
            []);
        return ThermalModelFitter.Fit(Identity, PowerBaseline(hardware), run, interaction: null);
    }
}
