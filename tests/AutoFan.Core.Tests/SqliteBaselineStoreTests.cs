using AutoFan.Core;
using AutoFan.Hardware;
using AutoFan.Storage;
using Microsoft.Data.Sqlite;

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

    [Fact]
    public void Memory_round_trip_keeps_everyday_and_calibrated_low_heat_profiles()
    {
        using var store = new SqliteBaselineStore("Data Source=:memory:");
        HeatProfile low = new(HeatProfile.EverydayCpuWorkers, 2560, 1440, 1, 48);
        Assert.NotEqual(HeatProfile.DefaultLow, low);
        BaselineRun original = SampleRun(ambient: 21.0, evidence: MetricEvidence.Measured) with
        {
            EverydayProfile = HeatProfile.Everyday,
            LowProfile = low,
        };

        store.Save(original);
        BaselineRun? loaded = store.GetLatest();

        Assert.NotNull(loaded);
        AssertEqualRuns(original, loaded);
        Assert.Equal(HeatProfile.Everyday, loaded.EverydayProfile);
        Assert.Equal(low, loaded.LowProfile);
        Assert.NotEqual(HeatProfile.DefaultLow, loaded.LowProfile);
    }

    [Fact]
    public void Old_row_without_heat_columns_loads_null_profiles_not_default_low()
    {
        string path = Path.Combine(Path.GetTempPath(), $"autofan-baseline-old-{Guid.NewGuid():N}.db");
        try
        {
            string connection = $"Data Source={path};Pooling=False";
            using (var raw = new SqliteConnection(connection))
            {
                raw.Open();
                using var command = raw.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE baseline_run (
                        id TEXT PRIMARY KEY,
                        started_utc TEXT NOT NULL,
                        finished_utc TEXT NOT NULL,
                        status TEXT NOT NULL,
                        abort_detail TEXT,
                        ambient_celsius REAL,
                        gpu_load_available INTEGER NOT NULL
                    );
                    INSERT INTO baseline_run (
                        id, started_utc, finished_utc, status, abort_detail, ambient_celsius, gpu_load_available)
                    VALUES (
                        'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
                        '2026-09-19T16:00:00.0000000+00:00',
                        '2026-09-19T16:05:00.0000000+00:00',
                        'Completed',
                        NULL,
                        NULL,
                        1);
                    """;
                command.ExecuteNonQuery();
            }

            using var store = new SqliteBaselineStore(connection);
            BaselineRun? loaded = store.GetLatest();

            Assert.NotNull(loaded);
            Assert.Null(loaded.EverydayProfile);
            Assert.Null(loaded.LowProfile);
            Assert.NotEqual(HeatProfile.DefaultLow, loaded.LowProfile);
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
        Assert.Equal(expected.EverydayProfile, actual.EverydayProfile);
        Assert.Equal(expected.LowProfile, actual.LowProfile);
    }
}
