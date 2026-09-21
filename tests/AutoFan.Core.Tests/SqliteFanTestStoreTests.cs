using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class SqliteFanTestStoreTests
{
    [Fact]
    public void Memory_round_trip_keeps_none_and_unknown_cells()
    {
        using var store = new SqliteFanTestStore("Data Source=:memory:");
        FanTestRun original = SampleRun();

        store.Save(original);
        FanTestRun? loaded = store.GetLatest();

        Assert.NotNull(loaded);
        AssertEqualRuns(original, loaded);
        Assert.Single(store.List());
        Assert.Contains(
            loaded.Influence,
            entry => entry.Effect == InfluenceEffect.None && entry.Evidence == MetricEvidence.Measured);
        Assert.Contains(
            loaded.Influence,
            entry => entry.Evidence == MetricEvidence.Unknown && entry.Target == InfluenceTarget.Case);
        Assert.Contains(loaded.Skipped, skip => skip.FanGroupId == FakeHardwareBackend.PumpId);
    }

    [Fact]
    public void File_round_trip_keeps_influence_and_skips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-fantest-{Guid.NewGuid():N}.db");
        try
        {
            FanTestRun original = SampleRun();
            string connection = $"Data Source={path};Pooling=False";
            using (var store = new SqliteFanTestStore(connection))
            {
                store.Save(original);
            }

            using var reopened = new SqliteFanTestStore(connection);
            FanTestRun? loaded = reopened.GetLatest();

            Assert.NotNull(loaded);
            AssertEqualRuns(original, loaded);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static FanTestRun SampleRun()
    {
        var hardware = new FakeHardwareBackend();
        HardwareSnapshot snapshot = hardware.ReadSnapshot();
        DateTimeOffset started = new(2026, 9, 19, 18, 0, 0, TimeSpan.Zero);
        FanTestSample[] samples =
        [
            new FanTestSample(
                started,
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                FanTestStage.Reference,
                snapshot,
                Settled: true),
            new FanTestSample(
                started.AddSeconds(1),
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                FanTestStage.Perturb,
                snapshot,
                Settled: false,
                HeatId: HeatId.Everyday),
        ];
        InfluenceEntry[] influence =
        [
            new InfluenceEntry(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Cpu,
                0.2,
                InfluenceEffect.None,
                MetricEvidence.Measured,
                35,
                60,
                490,
                840),
            new InfluenceEntry(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Gpu,
                3.2,
                InfluenceEffect.High,
                MetricEvidence.Measured,
                35,
                60,
                490,
                840),
            new InfluenceEntry(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Vrm,
                1.1,
                InfluenceEffect.Low,
                MetricEvidence.Measured,
                35,
                60,
                490,
                840),
            new InfluenceEntry(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                InfluenceTarget.Case,
                null,
                Effect: null,
                MetricEvidence.Unknown,
                35,
                60,
                490,
                840),
        ];
        SkippedFanGroup[] skipped =
        [
            new SkippedFanGroup(FakeHardwareBackend.PumpId, "AIO pump", FanTestReasons.Pump),
        ];

        return new FanTestRun(
            Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            started,
            started.AddMinutes(8),
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            samples,
            influence,
            skipped);
    }

    private static void AssertEqualRuns(FanTestRun expected, FanTestRun actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.StartedAt, actual.StartedAt);
        Assert.Equal(expected.FinishedAt, actual.FinishedAt);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.AbortDetail, actual.AbortDetail);
        Assert.Equal(expected.GpuLoadAvailable, actual.GpuLoadAvailable);
        Assert.Equal(expected.Samples.Count, actual.Samples.Count);
        Assert.Equal(expected.Samples[0].FanGroupId, actual.Samples[0].FanGroupId);
        Assert.Equal(expected.Samples[0].Stage, actual.Samples[0].Stage);
        Assert.Equal(expected.Samples[0].Settled, actual.Samples[0].Settled);
        Assert.Equal(expected.Samples[0].HeatId, actual.Samples[0].HeatId);
        Assert.Equal(expected.Samples[1].Settled, actual.Samples[1].Settled);
        Assert.Equal(expected.Samples[1].HeatId, actual.Samples[1].HeatId);
        Assert.Equal(expected.Influence.Count, actual.Influence.Count);
        Assert.Equal(expected.Influence[0].Effect, actual.Influence[0].Effect);
        Assert.Equal(expected.Influence[0].DeltaCelsius, actual.Influence[0].DeltaCelsius);
        Assert.Equal(expected.Influence[3].Evidence, actual.Influence[3].Evidence);
        Assert.Equal(expected.Skipped.Count, actual.Skipped.Count);
        Assert.Equal(expected.Skipped[0].Reason, actual.Skipped[0].Reason);
    }
}
