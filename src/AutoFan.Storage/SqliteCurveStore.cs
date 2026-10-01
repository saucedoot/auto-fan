using System.Globalization;
using AutoFan.Core;
using Microsoft.Data.Sqlite;

namespace AutoFan.Storage;

/// <summary>
/// Curve drafts live in the local database. Version 1 is the first numbered
/// migration. Later curve columns go through a new version, not a one-off add.
/// </summary>
public sealed class SqliteCurveStore : IDisposable
{
    public const int CurrentVersion = 1;

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public SqliteCurveStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        Migrate();
    }

    public static SqliteCurveStore OpenLocalAppData()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUTO Fan");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "autofan.db");
        return new SqliteCurveStore($"Data Source={path}");
    }

    public int SchemaVersion
    {
        get
        {
            lock (_gate)
            {
                return ReadVersion();
            }
        }
    }

    public void Save(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.State == ProfileState.Validated)
        {
            throw new InvalidOperationException("A curve draft cannot be saved as validated.");
        }

        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            using (var command = _connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO cooling_profile (id, created_utc, state, detail)
                    VALUES ($id, $created, $state, $detail);
                    """;
                command.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
                command.Parameters.AddWithValue("$created", profile.CreatedAt.ToString("O"));
                command.Parameters.AddWithValue("$state", profile.State.ToString());
                command.Parameters.AddWithValue("$detail", profile.Detail);
                command.ExecuteNonQuery();
            }

            foreach (CurvePoint point in profile.Points)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO curve_point (
                        profile_id, group_id, sensor, temperature_c, duty_percent, evidence, origin)
                    VALUES ($profile, $group, $sensor, $temperature, $duty, $evidence, $origin);
                    """;
                command.Parameters.AddWithValue("$profile", profile.Id.ToString("D"));
                command.Parameters.AddWithValue("$group", point.GroupId);
                command.Parameters.AddWithValue("$sensor", point.Sensor.ToString());
                command.Parameters.AddWithValue("$temperature", point.TemperatureCelsius);
                command.Parameters.AddWithValue("$duty", point.DutyPercent);
                command.Parameters.AddWithValue("$evidence", point.Evidence.ToString());
                command.Parameters.AddWithValue("$origin", point.Origin.ToString());
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public CoolingProfile? GetLatest()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id FROM cooling_profile ORDER BY rowid DESC LIMIT 1;";
            object? value = command.ExecuteScalar();
            return value is string id ? Load(Guid.Parse(id)) : null;
        }
    }

    public void Dispose() => _connection.Dispose();

    private void Migrate()
    {
        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS schema_migration (
                    version INTEGER PRIMARY KEY,
                    applied_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        if (ReadVersion() >= CurrentVersion)
        {
            return;
        }

        using var migration = _connection.CreateCommand();
        migration.CommandText =
            """
            CREATE TABLE IF NOT EXISTS cooling_profile (
                id TEXT PRIMARY KEY,
                created_utc TEXT NOT NULL,
                state TEXT NOT NULL,
                detail TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS curve_point (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                profile_id TEXT NOT NULL,
                group_id TEXT NOT NULL,
                sensor TEXT NOT NULL,
                temperature_c REAL NOT NULL,
                duty_percent INTEGER NOT NULL,
                evidence TEXT NOT NULL,
                origin TEXT NOT NULL,
                FOREIGN KEY (profile_id) REFERENCES cooling_profile(id)
            );
            INSERT INTO schema_migration (version, applied_utc) VALUES (1, $applied);
            """;
        migration.Parameters.AddWithValue("$applied", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        migration.ExecuteNonQuery();
    }

    private int ReadVersion()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migration;";
        try
        {
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private CoolingProfile Load(Guid id)
    {
        using var profileCommand = _connection.CreateCommand();
        profileCommand.CommandText =
            """
            SELECT created_utc, state, detail
            FROM cooling_profile WHERE id = $id;
            """;
        profileCommand.Parameters.AddWithValue("$id", id.ToString("D"));
        using SqliteDataReader profileReader = profileCommand.ExecuteReader();
        if (!profileReader.Read())
        {
            throw new InvalidOperationException($"Cooling profile '{id}' was not found.");
        }

        DateTimeOffset created = DateTimeOffset.Parse(
            profileReader.GetString(0),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        var state = Enum.Parse<ProfileState>(profileReader.GetString(1));
        string detail = profileReader.GetString(2);
        profileReader.Close();

        var points = new List<CurvePoint>();
        using var pointCommand = _connection.CreateCommand();
        pointCommand.CommandText =
            """
            SELECT group_id, sensor, temperature_c, duty_percent, evidence, origin
            FROM curve_point WHERE profile_id = $id ORDER BY id;
            """;
        pointCommand.Parameters.AddWithValue("$id", id.ToString("D"));
        using SqliteDataReader reader = pointCommand.ExecuteReader();
        while (reader.Read())
        {
            points.Add(new CurvePoint(
                reader.GetString(0),
                Enum.Parse<CurveSensor>(reader.GetString(1)),
                reader.GetDouble(2),
                reader.GetInt32(3),
                Enum.Parse<MetricEvidence>(reader.GetString(4)),
                Enum.Parse<CurvePointOrigin>(reader.GetString(5))));
        }

        return new CoolingProfile(id, created, state, detail, points);
    }
}
