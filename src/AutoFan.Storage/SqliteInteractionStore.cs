using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoFan.Core;
using Microsoft.Data.Sqlite;

namespace AutoFan.Storage;

public sealed class SqliteInteractionStore : IInteractionStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public SqliteInteractionStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    public static SqliteInteractionStore OpenLocalAppData()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUTO Fan");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "autofan.db");
        return new SqliteInteractionStore($"Data Source={path}");
    }

    public void Save(InteractionRun run)
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
                    INSERT INTO interaction_run (
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

            foreach (InteractionSample sample in run.Samples)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO interaction_sample (
                        run_id, captured_utc, first_group_id, first_group_name,
                        second_group_id, second_group_name, step, snapshot_json)
                    VALUES ($run, $captured, $first, $firstName, $second, $secondName, $step, $snapshot);
                    """;
                command.Parameters.AddWithValue("$run", run.Id.ToString("D"));
                command.Parameters.AddWithValue("$captured", sample.CapturedAt.ToString("O"));
                command.Parameters.AddWithValue("$first", sample.FirstGroupId);
                command.Parameters.AddWithValue("$firstName", sample.FirstGroupName);
                command.Parameters.AddWithValue("$second", sample.SecondGroupId);
                command.Parameters.AddWithValue("$secondName", sample.SecondGroupName);
                command.Parameters.AddWithValue("$step", sample.Step.ToString());
                command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(sample.Snapshot, JsonOptions));
                command.ExecuteNonQuery();
            }

            foreach (InteractionEntry entry in run.Effects)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO interaction_entry (
                        run_id, first_group_id, first_group_name, second_group_id, second_group_name,
                        target, first_delta, second_delta, combined_delta, residual, evidence, inferred_note)
                    VALUES (
                        $run, $first, $firstName, $second, $secondName,
                        $target, $firstDelta, $secondDelta, $combined, $residual, $evidence, $note);
                    """;
                command.Parameters.AddWithValue("$run", run.Id.ToString("D"));
                command.Parameters.AddWithValue("$first", entry.FirstGroupId);
                command.Parameters.AddWithValue("$firstName", entry.FirstGroupName);
                command.Parameters.AddWithValue("$second", entry.SecondGroupId);
                command.Parameters.AddWithValue("$secondName", entry.SecondGroupName);
                command.Parameters.AddWithValue("$target", entry.Target.ToString());
                command.Parameters.AddWithValue("$firstDelta", (object?)entry.FirstDeltaCelsius ?? DBNull.Value);
                command.Parameters.AddWithValue("$secondDelta", (object?)entry.SecondDeltaCelsius ?? DBNull.Value);
                command.Parameters.AddWithValue("$combined", (object?)entry.CombinedDeltaCelsius ?? DBNull.Value);
                command.Parameters.AddWithValue("$residual", (object?)entry.ResidualCelsius ?? DBNull.Value);
                command.Parameters.AddWithValue("$evidence", entry.Evidence.ToString());
                command.Parameters.AddWithValue("$note", (object?)entry.InferredNote ?? DBNull.Value);
                command.ExecuteNonQuery();
            }

            foreach (SkippedFanGroup skip in run.Skipped)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO interaction_skip (run_id, fan_group_id, fan_group_name, reason)
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

    public InteractionRun? GetLatest()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id FROM interaction_run ORDER BY rowid DESC LIMIT 1;";
            object? value = command.ExecuteScalar();
            if (value is not string id)
            {
                return null;
            }

            return Load(Guid.Parse(id));
        }
    }

    public IReadOnlyList<InteractionRun> List()
    {
        lock (_gate)
        {
            var ids = new List<Guid>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT id FROM interaction_run ORDER BY rowid;";
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
            CREATE TABLE IF NOT EXISTS interaction_run (
                id TEXT PRIMARY KEY,
                started_utc TEXT NOT NULL,
                finished_utc TEXT NOT NULL,
                status TEXT NOT NULL,
                abort_detail TEXT,
                gpu_load_available INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS interaction_sample (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                captured_utc TEXT NOT NULL,
                first_group_id TEXT NOT NULL,
                first_group_name TEXT NOT NULL,
                second_group_id TEXT NOT NULL,
                second_group_name TEXT NOT NULL,
                step TEXT NOT NULL,
                snapshot_json TEXT NOT NULL,
                FOREIGN KEY (run_id) REFERENCES interaction_run(id)
            );
            CREATE TABLE IF NOT EXISTS interaction_entry (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                first_group_id TEXT NOT NULL,
                first_group_name TEXT NOT NULL,
                second_group_id TEXT NOT NULL,
                second_group_name TEXT NOT NULL,
                target TEXT NOT NULL,
                first_delta REAL,
                second_delta REAL,
                combined_delta REAL,
                residual REAL,
                evidence TEXT NOT NULL,
                inferred_note TEXT,
                FOREIGN KEY (run_id) REFERENCES interaction_run(id)
            );
            CREATE TABLE IF NOT EXISTS interaction_skip (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                fan_group_id TEXT NOT NULL,
                fan_group_name TEXT NOT NULL,
                reason TEXT NOT NULL,
                FOREIGN KEY (run_id) REFERENCES interaction_run(id)
            );
            """;
        command.ExecuteNonQuery();
    }

    private InteractionRun Load(Guid id)
    {
        string idText = id.ToString("D");
        using var runCommand = _connection.CreateCommand();
        runCommand.CommandText =
            """
            SELECT started_utc, finished_utc, status, abort_detail, gpu_load_available
            FROM interaction_run WHERE id = $id;
            """;
        runCommand.Parameters.AddWithValue("$id", idText);
        using SqliteDataReader runReader = runCommand.ExecuteReader();
        if (!runReader.Read())
        {
            throw new InvalidOperationException($"Interaction run '{idText}' was not found.");
        }

        DateTimeOffset started = ParseTime(runReader.GetString(0));
        DateTimeOffset finished = ParseTime(runReader.GetString(1));
        var status = Enum.Parse<FanTestRunStatus>(runReader.GetString(2));
        string? abort = runReader.IsDBNull(3) ? null : runReader.GetString(3);
        bool gpu = runReader.GetInt32(4) != 0;
        runReader.Close();

        var samples = new List<InteractionSample>();
        using (var sampleCommand = _connection.CreateCommand())
        {
            sampleCommand.CommandText =
                """
                SELECT captured_utc, first_group_id, first_group_name, second_group_id,
                       second_group_name, step, snapshot_json
                FROM interaction_sample WHERE run_id = $id ORDER BY id;
                """;
            sampleCommand.Parameters.AddWithValue("$id", idText);
            using SqliteDataReader reader = sampleCommand.ExecuteReader();
            while (reader.Read())
            {
                HardwareSnapshot? snapshot = JsonSerializer.Deserialize<HardwareSnapshot>(
                    reader.GetString(6),
                    JsonOptions);
                if (snapshot is null)
                {
                    throw new InvalidOperationException("A stored snapshot could not be read.");
                }

                samples.Add(new InteractionSample(
                    ParseTime(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    Enum.Parse<InteractionStep>(reader.GetString(5)),
                    snapshot));
            }
        }

        var effects = new List<InteractionEntry>();
        using (var effectCommand = _connection.CreateCommand())
        {
            effectCommand.CommandText =
                """
                SELECT first_group_id, first_group_name, second_group_id, second_group_name, target,
                       first_delta, second_delta, combined_delta, residual, evidence, inferred_note
                FROM interaction_entry WHERE run_id = $id ORDER BY id;
                """;
            effectCommand.Parameters.AddWithValue("$id", idText);
            using SqliteDataReader reader = effectCommand.ExecuteReader();
            while (reader.Read())
            {
                effects.Add(new InteractionEntry(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    Enum.Parse<InfluenceTarget>(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    Enum.Parse<MetricEvidence>(reader.GetString(9)),
                    reader.IsDBNull(10) ? null : reader.GetString(10)));
            }
        }

        var skipped = new List<SkippedFanGroup>();
        using (var skipCommand = _connection.CreateCommand())
        {
            skipCommand.CommandText =
                """
                SELECT fan_group_id, fan_group_name, reason
                FROM interaction_skip WHERE run_id = $id ORDER BY id;
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

        return new InteractionRun(id, started, finished, status, abort, gpu, samples, effects, skipped);
    }

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
