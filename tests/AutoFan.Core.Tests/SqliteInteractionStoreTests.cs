using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class SqliteInteractionStoreTests
{
    [Fact]
    public void Memory_round_trip_keeps_residual_and_inferred_note()
    {
        using var store = new SqliteInteractionStore("Data Source=:memory:");
        InteractionRun original = SampleRun();

        store.Save(original);
        InteractionRun? loaded = store.GetLatest();

        Assert.NotNull(loaded);
        AssertEqualRuns(original, loaded);
        Assert.Equal(MetricEvidence.Measured, loaded.Effects[0].Evidence);
        Assert.Contains("GPU", loaded.Effects[0].InferredNote, StringComparison.Ordinal);
    }

    [Fact]
    public void File_round_trip_keeps_unknown_and_skips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-interaction-{Guid.NewGuid():N}.db");
        try
        {
            InteractionRun original = SampleRun();
            string connection = $"Data Source={path};Pooling=False";
            using (var store = new SqliteInteractionStore(connection))
            {
                store.Save(original);
            }

            using var reopened = new SqliteInteractionStore(connection);
            InteractionRun? loaded = reopened.GetLatest();

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

    private static InteractionRun SampleRun()
    {
        var hardware = new FakeHardwareBackend();
        HardwareSnapshot snapshot = hardware.ReadSnapshot();
        DateTimeOffset started = new(2026, 9, 19, 21, 0, 0, TimeSpan.Zero);
        InteractionSample[] samples =
        [
            new(
                started,
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                FakeHardwareBackend.TopFanId,
                "Top exhaust",
                InteractionStep.ReferenceFirst,
                snapshot),
        ];
        InteractionEntry[] effects =
        [
            new InteractionEntry(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                FakeHardwareBackend.TopFanId,
                "Top exhaust",
                InfluenceTarget.Gpu,
                1.5,
                0.7,
                3.8,
                1.6,
                MetricEvidence.Measured,
                InteractionBuilder.InferNote(1.6, InfluenceTarget.Gpu)),
            new InteractionEntry(
                FakeHardwareBackend.FrontFanId,
                "Front intake",
                FakeHardwareBackend.TopFanId,
                "Top exhaust",
                InfluenceTarget.Case,
                null,
                null,
                null,
                null,
                MetricEvidence.Unknown,
                InferredNote: null),
        ];

        return new InteractionRun(
            Guid.Parse("cccccccc-dddd-eeee-ffff-000000000001"),
            started,
            started.AddMinutes(10),
            FanTestRunStatus.Completed,
            AbortDetail: null,
            GpuLoadAvailable: true,
            samples,
            effects,
            [new SkippedFanGroup(FakeHardwareBackend.PumpId, "AIO pump", FanTestReasons.Pump)]);
    }

    private static void AssertEqualRuns(InteractionRun expected, InteractionRun actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Effects.Count, actual.Effects.Count);
        Assert.Equal(expected.Effects[0].ResidualCelsius, actual.Effects[0].ResidualCelsius);
        Assert.Equal(expected.Effects[0].Evidence, actual.Effects[0].Evidence);
        Assert.Equal(expected.Effects[0].InferredNote, actual.Effects[0].InferredNote);
        Assert.Equal(expected.Effects[1].Evidence, actual.Effects[1].Evidence);
        Assert.Equal(expected.Skipped[0].Reason, actual.Skipped[0].Reason);
        Assert.Equal(expected.Samples[0].Step, actual.Samples[0].Step);
    }
}
