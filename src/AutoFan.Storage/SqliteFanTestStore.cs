using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoFan.Core;
using Microsoft.Data.Sqlite;

namespace AutoFan.Storage;

public sealed class SqliteFanTestStore : IFanTestStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public SqliteFanTestStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    public static SqliteFanTestStore OpenLocalAppData()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUTO Fan");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "autofan.db");
        return new SqliteFanTestStore($"Data Source={path}");
    }

    public void Save(FanTestRun run)
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
                    INSERT INTO fan_test_run (
                        id, started_utc, finished_utc, status, abort_detail, gpu_load_available)
                    VALUES ($id, $started, $finished, $status, $abort, $gpu);
                    """;
                command.Parameters.AddWithValue("$id", run.Id.ToString("D"));
                command.Parameters.AddWithValue("$started", run.StartedAt.ToString("O"));
                command.Parameters.AddWithValue("$finished", run.FinishedAt.ToString("O"));
                command.Parameters.AddWithValue("$status", run.Status.ToString());
                command.Parameters.AddWithValue("$abort", (object?)run.AbortDetail ?? DBNull.Value);
                command.Parameters.AddWithValue("$gpu", run.GpuLoadAvailable ? 1 : 0);
                command.ExecuteNonQuery();
            }

            foreach (FanTestSample sample in run.Samples)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO fan_test_sample (
                        run_id, captured_utc, fan_group_id, fan_group_name, stage, snapshot_json, settled)
                    VALUES ($run, $captured, $group, $name, $stage, $snapshot, $settled);
                    """;
                command.Parameters.AddWithValue("$run", run.Id.ToString("D"));
                command.Parameters.AddWithValue("$captured", sample.CapturedAt.ToString("O"));
                command.Parameters.AddWithValue("$group", sample.FanGroupId);
                command.Parameters.AddWithValue("$name", sample.FanGroupName);
                command.Parameters.AddWithValue("$stage", sample.Stage.ToString());
                command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(sample.Snapshot, JsonOptions));
                command.Parameters.AddWithValue("$settled", sample.Settled ? 1 : 0);
                command.ExecuteNonQuery();
            }

            foreach (InfluenceEntry entry in run.Influence)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO influence_entry (
                        run_id, fan_group_id, fan_group_name, target, delta_celsius, effect,
                        evidence, duty_before, duty_after, rpm_before, rpm_after, skip_reason)
                    VALUES (
                        $run, $group, $name, $target, $delta, $effect,
                        $evidence, $dutyBefore, $dutyAfter, $rpmBefore, $rpmAfter, $skip);
                    """;
                command.Parameters.AddWithValue("$run", run.Id.ToString("D"));
                command.Parameters.AddWithValue("$group", entry.FanGroupId);
                command.Parameters.AddWithValue("$name", entry.FanGroupName);
                command.Parameters.AddWithValue("$target", entry.Target.ToString());
                command.Parameters.AddWithValue("$delta", (object?)entry.DeltaCelsius ?? DBNull.Value);
                command.Parameters.AddWithValue("$effect", (object?)entry.Effect?.ToString() ?? DBNull.Value);
                command.Parameters.AddWithValue("$evidence", entry.Evidence.ToString());
                command.Parameters.AddWithValue("$dutyBefore", (object?)entry.DutyBefore ?? DBNull.Value);
                command.Parameters.AddWithValue("$dutyAfter", (object?)entry.DutyAfter ?? DBNull.Value);
                command.Parameters.AddWithValue("$rpmBefore", (object?)entry.RpmBefore ?? DBNull.Value);
                command.Parameters.AddWithValue("$rpmAfter", (object?)entry.RpmAfter ?? DBNull.Value);
                command.Parameters.AddWithValue("$skip", (object?)entry.SkipReason ?? DBNull.Value);
                command.ExecuteNonQuery();
            }

            foreach (SkippedFanGroup skip in run.Skipped)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO fan_test_skip (run_id, fan_group_id, fan_group_name, reason)
                    VALUES ($run, $group, $name, $reason);
                    """;
                command.Parameters.AddWithValue("$run", run.Id.ToString("D"));
                command.Parameters.AddWithValue("$group", skip.FanGroupId);
                command.Parameters.AddWithValue("$name", skip.FanGroupName);
                command.Parameters.AddWithValue("$reason", skip.Reason);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public FanTestRun? GetLatest()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id FROM fan_test_run ORDER BY rowid DESC LIMIT 1;";
            object? value = command.ExecuteScalar();
            if (value is not string id)
            {
                return null;
            }

            return Load(Guid.Parse(id));
        }
    }

    public IReadOnlyList<FanTestRun> List()
    {
        lock (_gate)
        {
            var ids = new List<Guid>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT id FROM fan_test_run ORDER BY rowid;";
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
            CREATE TABLE IF NOT EXISTS fan_test_run (
                id TEXT PRIMARY KEY,
                started_utc TEXT NOT NULL,
                finished_utc TEXT NOT NULL,
                status TEXT NOT NULL,
                abort_detail TEXT,
                gpu_load_available INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS fan_test_sample (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                captured_utc TEXT NOT NULL,
                fan_group_id TEXT NOT NULL,
                fan_group_name TEXT NOT NULL,
                stage TEXT NOT NULL,
                snapshot_json TEXT NOT NULL,
                settled INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (run_id) REFERENCES fan_test_run(id)
            );
            CREATE TABLE IF NOT EXISTS influence_entry (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                fan_group_id TEXT NOT NULL,
                fan_group_name TEXT NOT NULL,
                target TEXT NOT NULL,
                delta_celsius REAL,
                effect TEXT,
                evidence TEXT NOT NULL,
                duty_before INTEGER,
                duty_after INTEGER,
                rpm_before REAL,
                rpm_after REAL,
                skip_reason TEXT,
                FOREIGN KEY (run_id) REFERENCES fan_test_run(id)
            );
            CREATE TABLE IF NOT EXISTS fan_test_skip (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                fan_group_id TEXT NOT NULL,
                fan_group_name TEXT NOT NULL,
                reason TEXT NOT NULL,
                FOREIGN KEY (run_id) REFERENCES fan_test_run(id)
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn("fan_test_sample", "settled", "INTEGER NOT NULL DEFAULT 0");
    }

    private void EnsureColumn(string table, string column, string sqlType)
    {
        using (var inspect = _connection.CreateCommand())
        {
            inspect.CommandText = $"PRAGMA table_info({table});";
            using SqliteDataReader reader = inspect.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {sqlType};";
        alter.ExecuteNonQuery();
    }

    private FanTestRun Load(Guid id)
    {
        string idText = id.ToString("D");
        using var runCommand = _connection.CreateCommand();
        runCommand.CommandText =
            """
            SELECT started_utc, finished_utc, status, abort_detail, gpu_load_available
            FROM fan_test_run WHERE id = $id;
            """;
        runCommand.Parameters.AddWithValue("$id", idText);
        using SqliteDataReader runReader = runCommand.ExecuteReader();
        if (!runReader.Read())
        {
            throw new InvalidOperationException($"Fan test run '{idText}' was not found.");
        }

        DateTimeOffset started = ParseTime(runReader.GetString(0));
        DateTimeOffset finished = ParseTime(runReader.GetString(1));
        var status = Enum.Parse<FanTestRunStatus>(runReader.GetString(2));
        string? abort = runReader.IsDBNull(3) ? null : runReader.GetString(3);
        bool gpu = runReader.GetInt32(4) != 0;
        runReader.Close();

        var samples = new List<FanTestSample>();
        using (var sampleCommand = _connection.CreateCommand())
        {
            sampleCommand.CommandText =
                """
                SELECT captured_utc, fan_group_id, fan_group_name, stage, snapshot_json, settled
                FROM fan_test_sample WHERE run_id = $id ORDER BY id;
                """;
            sampleCommand.Parameters.AddWithValue("$id", idText);
            using SqliteDataReader reader = sampleCommand.ExecuteReader();
            while (reader.Read())
            {
                DateTimeOffset captured = ParseTime(reader.GetString(0));
                string groupId = reader.GetString(1);
                string groupName = reader.GetString(2);
                var stage = Enum.Parse<FanTestStage>(reader.GetString(3));
                HardwareSnapshot? snapshot = JsonSerializer.Deserialize<HardwareSnapshot>(
                    reader.GetString(4),
                    JsonOptions);
                if (snapshot is null)
                {
                    throw new InvalidOperationException("A stored snapshot could not be read.");
                }

                samples.Add(new FanTestSample(
                    captured,
                    groupId,
                    groupName,
                    stage,
                    snapshot,
                    reader.GetInt32(5) != 0));
            }
        }

        var influence = new List<InfluenceEntry>();
        using (var influenceCommand = _connection.CreateCommand())
        {
            influenceCommand.CommandText =
                """
                SELECT fan_group_id, fan_group_name, target, delta_celsius, effect, evidence,
                       duty_before, duty_after, rpm_before, rpm_after, skip_reason
                FROM influence_entry WHERE run_id = $id ORDER BY id;
                """;
            influenceCommand.Parameters.AddWithValue("$id", idText);
            using SqliteDataReader reader = influenceCommand.ExecuteReader();
            while (reader.Read())
            {
                influence.Add(new InfluenceEntry(
                    reader.GetString(0),
                    reader.GetString(1),
                    Enum.Parse<InfluenceTarget>(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    reader.IsDBNull(4) ? null : Enum.Parse<InfluenceEffect>(reader.GetString(4)),
                    Enum.Parse<MetricEvidence>(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10)));
            }
        }

        var skipped = new List<SkippedFanGroup>();
        using (var skipCommand = _connection.CreateCommand())
        {
            skipCommand.CommandText =
                """
                SELECT fan_group_id, fan_group_name, reason
                FROM fan_test_skip WHERE run_id = $id ORDER BY id;
                """;
            skipCommand.Parameters.AddWithValue("$id", idText);
            using SqliteDataReader reader = skipCommand.ExecuteReader();
            while (reader.Read())
            {
                skipped.Add(new SkippedFanGroup(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)));
            }
        }

        return new FanTestRun(id, started, finished, status, abort, gpu, samples, influence, skipped);
    }

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
