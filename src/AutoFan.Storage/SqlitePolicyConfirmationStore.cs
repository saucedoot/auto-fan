using System.Globalization;
using AutoFan.Core;
using Microsoft.Data.Sqlite;

namespace AutoFan.Storage;

public sealed class SqlitePolicyConfirmationStore : IPolicyConfirmationStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public SqlitePolicyConfirmationStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    public static SqlitePolicyConfirmationStore OpenLocalAppData()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUTO Fan");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "autofan.db");
        return new SqlitePolicyConfirmationStore($"Data Source={path}");
    }

    public void Save(PolicyConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        PolicyConfirmation row = InMemoryPolicyConfirmationStore.WithIdentity(confirmation);
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO policy_confirmation (
                    id, observed_utc, measured_cpu, measured_gpu, expected_cpu, expected_gpu,
                    missed, added_airflow, ambient_celsius)
                VALUES ($id, $observed, $mcpu, $mgpu, $ecpu, $egpu, $missed, $added, $ambient);
                """;
            command.Parameters.AddWithValue("$id", row.Id!.Value.ToString("D"));
            command.Parameters.AddWithValue("$observed", row.ObservedAt!.Value.ToString("O"));
            command.Parameters.AddWithValue("$mcpu", (object?)row.MeasuredCpuCelsius ?? DBNull.Value);
            command.Parameters.AddWithValue("$mgpu", (object?)row.MeasuredGpuCelsius ?? DBNull.Value);
            command.Parameters.AddWithValue("$ecpu", (object?)row.ExpectedCpuCelsius ?? DBNull.Value);
            command.Parameters.AddWithValue("$egpu", (object?)row.ExpectedGpuCelsius ?? DBNull.Value);
            command.Parameters.AddWithValue("$missed", row.Missed ? 1 : 0);
            command.Parameters.AddWithValue("$added", row.AddedAirflow ? 1 : 0);
            command.Parameters.AddWithValue("$ambient", (object?)row.AmbientCelsius ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    public PolicyConfirmation? GetLatest()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id FROM policy_confirmation ORDER BY rowid DESC LIMIT 1;";
            object? value = command.ExecuteScalar();
            if (value is not string id)
            {
                return null;
            }

            return Load(Guid.Parse(id));
        }
    }

    public IReadOnlyList<PolicyConfirmation> List()
    {
        lock (_gate)
        {
            var ids = new List<Guid>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT id FROM policy_confirmation ORDER BY rowid;";
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
            CREATE TABLE IF NOT EXISTS policy_confirmation (
                id TEXT PRIMARY KEY,
                observed_utc TEXT NOT NULL,
                measured_cpu REAL,
                measured_gpu REAL,
                expected_cpu REAL,
                expected_gpu REAL,
                missed INTEGER NOT NULL,
                added_airflow INTEGER NOT NULL,
                ambient_celsius REAL
            );
            """;
        command.ExecuteNonQuery();
    }

    private PolicyConfirmation Load(Guid id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT observed_utc, measured_cpu, measured_gpu, expected_cpu, expected_gpu,
                   missed, added_airflow, ambient_celsius
            FROM policy_confirmation WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Policy confirmation '{id:D}' was not found.");
        }

        DateTimeOffset observed = DateTimeOffset.Parse(
            reader.GetString(0),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        return new PolicyConfirmation(
            reader.IsDBNull(1) ? null : reader.GetDouble(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.IsDBNull(4) ? null : reader.GetDouble(4),
            reader.GetInt32(5) != 0,
            reader.GetInt32(6) != 0,
            id,
            observed,
            reader.IsDBNull(7) ? null : reader.GetDouble(7));
    }
}
