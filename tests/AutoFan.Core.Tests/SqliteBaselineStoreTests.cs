using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;

namespace AutoFan.Core.Tests;

public sealed class SqliteBaselineStoreTests
{
    [Fact]
    public void Memory_round_trip_keeps_samples_metrics_and_unknown_ambient()
    {
        using var store = new SqliteBaselineStore("Data Source=:memory:");
        BaselineRun original = SampleRun(ambient: null, evidence: MetricEvidence.Unknown);

        store.Save(original);
        BaselineRun? loaded = store.GetLatest();

        Assert.NotNull(loaded);
        AssertEqualRuns(original, loaded);
        Assert.Single(store.List());
    }

    [Fact]
    public void File_round_trip_keeps_measured_ambient()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-baseline-{Guid.NewGuid():N}.db");
        try
        {
            BaselineRun original = SampleRun(ambient: 22.5, evidence: MetricEvidence.Measured);
            string connection = $"Data Source={path};Pooling=False";
            using (var store = new SqliteBaselineStore(connection))
            {
                store.Save(original);
            }

            using var reopened = new SqliteBaselineStore(connection);
            BaselineRun? loaded = reopened.GetLatest();

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

    private static BaselineRun SampleRun(double? ambient, MetricEvidence evidence)
    {
        var hardware = new FakeHardwareBackend();
        if (ambient is double value)
        {
            hardware.IncludeAmbient(value);
        }

        HardwareSnapshot snapshot = hardware.ReadSnapshot();
        DateTimeOffset started = new(2026, 9, 19, 16, 0, 0, TimeSpan.Zero);
        var samples = new List<BaselineSample>
        {
            new(started, BaselinePhase.Idle, snapshot),
            new(started.AddSeconds(1), BaselinePhase.High, snapshot),
            new(started.AddSeconds(2), BaselinePhase.Reference, snapshot),
        };
        BaselineMetric[] metrics =
        [
            new BaselineMetric(BaselineMetricNames.AmbientCelsius, ambient, "°C", evidence),
            new BaselineMetric(BaselineMetricNames.CpuRiseCelsius, 12.5, "°C", MetricEvidence.Measured),
            new BaselineMetric(
                BaselineMetricNames.CpuReferenceState,
                (int)SensorStability.Stable,
                string.Empty,
                MetricEvidence.Measured),
            new BaselineMetric(
                BaselineMetricNames.GpuReferenceState,
                (int)SensorStability.Drifting,
                string.Empty,
                MetricEvidence.Measured),
            new BaselineMetric(BaselineMetricNames.GpuMinimumDetectableCelsius, 0.7, "°C", MetricEvidence.Measured),
        ];

        return new BaselineRun(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            started,
            started.AddMinutes(5),
            BaselineRunStatus.Completed,
            AbortDetail: null,
            ambient,
            GpuLoadAvailable: true,
            samples,
            metrics);
    }

    private static void AssertEqualRuns(BaselineRun expected, BaselineRun actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.StartedAt, actual.StartedAt);
        Assert.Equal(expected.FinishedAt, actual.FinishedAt);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.AbortDetail, actual.AbortDetail);
        Assert.Equal(expected.AmbientCelsius, actual.AmbientCelsius);
        Assert.Equal(expected.GpuLoadAvailable, actual.GpuLoadAvailable);
        Assert.Equal(expected.Samples.Count, actual.Samples.Count);
        Assert.Equal(expected.Samples[0].Phase, actual.Samples[0].Phase);
        Assert.Equal(expected.Samples[0].Snapshot.Sensors.Count, actual.Samples[0].Snapshot.Sensors.Count);
        Assert.Equal(expected.Metrics.Count, actual.Metrics.Count);
        Assert.Equal(expected.Metrics[0].Evidence, actual.Metrics[0].Evidence);
        Assert.Equal(expected.Metrics[0].Value, actual.Metrics[0].Value);
    }
}
