namespace Planvexa.Database;

using System.Data;
using System.Reflection;
using DbUp;
using DbUp.Engine.Output;
using Npgsql;

public sealed record DatabaseUpgradeResult(bool Successful, IReadOnlyList<string> ExecutedScripts);

public sealed class DatabaseUpgradeException(string message, Exception? inner = null) : Exception(message, inner);

public static class PlanvexaDatabase
{
    private const int AdvisoryLockKey = 739_872_341;
    private const int StartupLockKey = 739_872_342;
    private const string JournalSchema = "platform";
    private const string JournalTable = "schema_versions";
    private static readonly Assembly ScriptsAssembly = typeof(PlanvexaDatabase).Assembly;

    public static readonly string[] ScriptNames = ScriptsAssembly
        .GetManifestResourceNames()
        .Where(name => name.Contains(".Scripts.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal)
        .ToArray();

    public static readonly string[] EfMigrationIds =
    [
        "20260730075533_AddChatRls",
    ];

    public static DatabaseUpgradeResult Upgrade(string connectionString, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        log ??= _ => { };

        EnsureDatabase.For.PostgresqlDatabase(connectionString);

        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        AcquireLock(connection);
        try
        {
            BootstrapJournal(connection);
            BridgeEfCreatedDatabaseIfNeeded(connection, log);

            var logSink = new ActionUpgradeLog(log);
            var upgrader = DeployChanges.To
                .PostgresqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(ScriptsAssembly, name => name.Contains(".Scripts.", StringComparison.Ordinal))
                .JournalToPostgresqlTable(JournalSchema, JournalTable)
                .WithTransactionPerScript()
                .LogTo(logSink)
                .Build();

            var result = upgrader.PerformUpgrade();
            if (!result.Successful)
            {
                throw new DatabaseUpgradeException("DbUp failed while applying Planvexa database scripts.", result.Error);
            }

            var executed = result.Scripts.Select(script => script.Name).ToArray();
            log($"DbUp completed successfully. Executed {executed.Length} script(s).");
            return new DatabaseUpgradeResult(true, executed);
        }
        finally
        {
            ReleaseLock(connection);
        }
    }

    /// <summary>
    /// Serializes one-time startup work (see PlanvexaBootstrap) across concurrently starting replicas.
    /// The lock is held for the lifetime of the returned handle — a PostgreSQL session advisory lock
    /// lives on its connection — so the caller must re-check its precondition INSIDE the lock: the
    /// replica that waited is looking at a database the winner has already changed.
    /// </summary>
    public static async Task<IAsyncDisposable> AcquireStartupLockAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT pg_advisory_lock({StartupLockKey});";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return new StartupLock(connection);
    }

    private sealed class StartupLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT pg_advisory_unlock({StartupLockKey});";
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                // Closing the session releases the lock anyway; the explicit unlock above just makes
                // that immediate rather than dependent on pooling.
                await connection.DisposeAsync();
            }
        }
    }

    private static void AcquireLock(NpgsqlConnection connection) => ExecuteNonQuery(connection, $"SELECT pg_advisory_lock({AdvisoryLockKey});");

    private static void ReleaseLock(NpgsqlConnection connection) => ExecuteNonQuery(connection, $"SELECT pg_advisory_unlock({AdvisoryLockKey});");

    private static void BootstrapJournal(NpgsqlConnection connection)
    {
        ExecuteNonQuery(connection, "CREATE SCHEMA IF NOT EXISTS platform;");
        ExecuteNonQuery(connection, """
            CREATE TABLE IF NOT EXISTS platform.schema_versions (
                scriptname character varying(255) NOT NULL PRIMARY KEY,
                applied timestamp without time zone NOT NULL
            );
            """);
    }

    private static void BridgeEfCreatedDatabaseIfNeeded(NpgsqlConnection connection, Action<string> log)
    {
        var journalCount = ExecuteScalar<long>(connection, "SELECT count(*) FROM platform.schema_versions;");
        if (journalCount > 0)
        {
            return;
        }

        var efHistoryExists = ExecuteScalar<bool>(connection, """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'platform' AND table_name = '__ef_migrations_history'
            );
            """);

        if (efHistoryExists)
        {
            var efMigrations = QueryStrings(connection, "SELECT migration_id FROM platform.__ef_migrations_history ORDER BY migration_id;");
            if (!EfMigrationIds.SequenceEqual(efMigrations, StringComparer.Ordinal))
            {
                throw new DatabaseUpgradeException(
                    $"Existing EF migration history does not match the supported Planvexa baseline. Found {efMigrations.Count} migration(s); expected {EfMigrationIds.Length}.");
            }

            foreach (var scriptName in ScriptNames)
            {
                ExecuteNonQuery(connection,
                    "INSERT INTO platform.schema_versions (scriptname, applied) VALUES (@scriptname, now()) ON CONFLICT (scriptname) DO NOTHING;",
                    command => command.Parameters.AddWithValue("scriptname", scriptName));
            }

            log($"Detected EF-created database with {efMigrations.Count} migration(s); baselined {ScriptNames.Length} DbUp script(s) without modifying tenant data.");
            return;
        }

        var knownTableExists = ExecuteScalar<bool>(connection, """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE (table_schema, table_name) IN (
                    ('tenancy', 'tenants'),
                    ('work', 'tasks'),
                    ('platform', 'outbox_messages')
                )
            );
            """);

        if (knownTableExists)
        {
            throw new DatabaseUpgradeException(
                "Database contains Planvexa-looking tables but no DbUp journal or supported EF migration history. Refusing to run baseline scripts against an unknown or partially migrated schema.");
        }
    }

    private static List<string> QueryStrings(NpgsqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static T ExecuteScalar<T>(NpgsqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = command.ExecuteScalar();
        if (result is null || result is DBNull)
        {
            throw new DataException($"Expected scalar result for SQL: {sql}");
        }

        return (T)result;
    }

    private static void ExecuteNonQuery(NpgsqlConnection connection, string sql, Action<NpgsqlCommand>? configure = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);
        command.ExecuteNonQuery();
    }

    private sealed class ActionUpgradeLog(Action<string> log) : IUpgradeLog
    {
        public void LogTrace(string format, params object[] args) => log("TRACE: " + string.Format(format, args));

        public void LogDebug(string format, params object[] args) => log("DEBUG: " + string.Format(format, args));

        public void LogInformation(string format, params object[] args) => log(string.Format(format, args));

        public void LogWarning(string format, params object[] args) => log("WARN: " + string.Format(format, args));

        public void LogError(string format, params object[] args) => log("ERROR: " + string.Format(format, args));

        public void LogError(Exception ex, string format, params object[] args) =>
            log("ERROR: " + string.Format(format, args) + " " + ex.Message);
    }
}
