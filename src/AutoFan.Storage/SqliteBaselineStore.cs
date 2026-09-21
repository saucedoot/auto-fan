using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoFan.Core;
using Microsoft.Data.Sqlite;

namespace AutoFan.Storage;

public sealed class SqliteBaselineStore : IBaselineStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public SqliteBaselineStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    public static SqliteBaselineStore OpenLocalAppData()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUTO Fan");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "autofan.db");
        return new SqliteBaselineStore($"Data Source={path}");
    }

    public void Save(BaselineRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO baseline_run (
                        id, started_utc, finished_utc, status, abort_detail, ambient_celsius, gpu_load_available)
                    VALUES ($id, $started, $finished, $status, $abort, $ambient, $gpu);
                    """;
                command.Parameters.AddWithValue("$id", run.Id.ToString("D"));
                command.Parameters.AddWithValue("$started", run.StartedAt.ToString("O"));
                command.Parameters.AddWithValue("$finished", run.FinishedAt.ToString("O"));
                command.Parameters.AddWithValue("$status", run.Status.ToString());
                command.Parameters.AddWithValue("$abort", (object?)run.AbortDetail ?? DBNull.Value);
                command.Parameters.AddWithValue("$ambient", (object?)run.AmbientCelsius ?? DBNull.Value);
                command.Parameters.AddWithValue("$gpu", run.GpuLoadAvailable ? 1 : 0);
                command.ExecuteNonQuery();
            }

            foreach (BaselineSample sample in run.Samples)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO baseline_sample (run_id, captured_utc, phase, snapshot_json)
                    VALUES ($run, $captured, $phase, $snapshot);
                    """;
                command.Parameters.AddWithValue("$run", run.Id.ToString("D"));
                command.Parameters.AddWithValue("$captured", sample.CapturedAt.ToString("O"));
                command.Parameters.AddWithValue("$phase", sample.Phase.ToString());
                command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(sample.Snapshot, JsonOptions));
                command.ExecuteNonQuery();
            }

            foreach (BaselineMetric metric in run.Metrics)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO baseline_metric (run_id, name, value, unit, evidence)
                    VALUES ($run, $name, $value, $unit, $evidence);
                    """;
                command.Parameters.AddWithValue("$run", run.Id.ToString("D"));
                command.Parameters.AddWithValue("$name", metric.Name);
                command.Parameters.AddWithValue("$value", (object?)metric.Value ?? DBNull.Value);
                command.Parameters.AddWithValue("$unit", metric.Unit);
                command.Parameters.AddWithValue("$evidence", metric.Evidence.ToString());
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public BaselineRun? GetLatest()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id FROM baseline_run ORDER BY rowid DESC LIMIT 1;";
            object? value = command.ExecuteScalar();
            if (value is not string id)
            {
                return null;
            }

            return Load(Guid.Parse(id));
        }
    }

    public IReadOnlyList<BaselineRun> List()
    {
        lock (_gate)
        {
            var ids = new List<Guid>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT id FROM baseline_run ORDER BY rowid;";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    ids.Add(Guid.Parse(reader.GetString(0)));
                }
            }

            return ids.Select(Load).ToArray();
        }
    }

    public void Dispose() => _connection.Dispose();

    private void EnsureSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS baseline_run (
                id TEXT PRIMARY KEY,
                started_utc TEXT NOT NULL,
                finished_utc TEXT NOT NULL,
                status TEXT NOT NULL,
                abort_detail TEXT,
                ambient_celsius REAL,
                gpu_load_available INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS baseline_sample (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                captured_utc TEXT NOT NULL,
                phase TEXT NOT NULL,
                snapshot_json TEXT NOT NULL,
                FOREIGN KEY (run_id) REFERENCES baseline_run(id)
            );
            CREATE TABLE IF NOT EXISTS baseline_metric (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                name TEXT NOT NULL,
                value REAL,
                unit TEXT NOT NULL,
                evidence TEXT NOT NULL,
                FOREIGN KEY (run_id) REFERENCES baseline_run(id)
            );
            """;
        command.ExecuteNonQuery();
    }

    private BaselineRun Load(Guid id)
    {
        string idText = id.ToString("D");
        using var runCommand = _connection.CreateCommand();
        runCommand.CommandText =
            """
            SELECT started_utc, finished_utc, status, abort_detail, ambient_celsius, gpu_load_available
            FROM baseline_run WHERE id = $id;
            """;
        runCommand.Parameters.AddWithValue("$id", idText);
        using SqliteDataReader runReader = runCommand.ExecuteReader();
        if (!runReader.Read())
        {
            throw new InvalidOperationException($"Baseline run '{idText}' was not found.");
        }

        DateTimeOffset started = ParseTime(runReader.GetString(0));
        DateTimeOffset finished = ParseTime(runReader.GetString(1));
        var status = Enum.Parse<BaselineRunStatus>(runReader.GetString(2));
        string? abort = runReader.IsDBNull(3) ? null : runReader.GetString(3);
        double? ambient = runReader.IsDBNull(4) ? null : runReader.GetDouble(4);
        bool gpu = runReader.GetInt32(5) != 0;
        runReader.Close();

        var samples = new List<BaselineSample>();
        using (var sampleCommand = _connection.CreateCommand())
        {
            sampleCommand.CommandText =
                """
                SELECT captured_utc, phase, snapshot_json
                FROM baseline_sample WHERE run_id = $id ORDER BY id;
                """;
            sampleCommand.Parameters.AddWithValue("$id", idText);
            using SqliteDataReader reader = sampleCommand.ExecuteReader();
            while (reader.Read())
            {
                DateTimeOffset captured = ParseTime(reader.GetString(0));
                var phase = Enum.Parse<BaselinePhase>(reader.GetString(1));
                HardwareSnapshot? snapshot = JsonSerializer.Deserialize<HardwareSnapshot>(reader.GetString(2), JsonOptions);
                if (snapshot is null)
                {
                    throw new InvalidOperationException("A stored snapshot could not be read.");
                }

                samples.Add(new BaselineSample(captured, phase, snapshot));
            }
        }

        var metrics = new List<BaselineMetric>();
        using (var metricCommand = _connection.CreateCommand())
        {
            metricCommand.CommandText =
                """
                SELECT name, value, unit, evidence
                FROM baseline_metric WHERE run_id = $id ORDER BY id;
                """;
            metricCommand.Parameters.AddWithValue("$id", idText);
            using SqliteDataReader reader = metricCommand.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.GetString(0);
                double? value = reader.IsDBNull(1) ? null : reader.GetDouble(1);
                string unit = reader.GetString(2);
                var evidence = Enum.Parse<MetricEvidence>(reader.GetString(3));
                metrics.Add(new BaselineMetric(name, value, unit, evidence));
            }
        }

        return new BaselineRun(id, started, finished, status, abort, ambient, gpu, samples, metrics);
    }

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
