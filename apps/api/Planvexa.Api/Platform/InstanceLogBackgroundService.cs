namespace Planvexa.Api.Platform;

using Npgsql;
using NpgsqlTypes;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Platform;

/// <summary>
/// Drains <see cref="InstanceLogProvider"/>'s queue into <c>platform.instance_logs</c> and purges
/// records past the retention window.
///
/// Raw Npgsql rather than the <c>PlanvexaDbContext</c>, on purpose: this is a singleton with no
/// request scope, and routing log writes through the request DbContext would enlist them in whatever
/// transaction happened to be open and re-stamp RLS session variables for a table that has none. It
/// also keeps the write path off EF entirely, which matters because an EF or Npgsql warning raised
/// while persisting a log record would otherwise queue another log record (the provider's category
/// exclusions are the other half of that guard).
///
/// Nothing in this class may throw its way out of <c>ExecuteAsync</c>: losing log persistence is
/// acceptable, taking the host down because logging failed is not. Every failure is swallowed after
/// being written to the console logger — which this provider deliberately does not capture.
/// </summary>
public sealed class InstanceLogBackgroundService(
    InstanceLogProvider provider,
    IClock clock,
    IConfiguration configuration,
    ILogger<InstanceLogBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(1);
    private const int MaxBatchSize = 500;

    private readonly string _connectionString = configuration.GetConnectionString("Planvexa")
        ?? "Host=localhost;Port=5432;Database=planvexa;Username=planvexa;Password=planvexa";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!provider.Options.Enabled)
        {
            logger.LogInformation("Instance log capture is disabled (InstanceLogs:Enabled=false).");
            return;
        }

        // Let start-up (and DbUp) finish before the first insert — the table may not exist yet.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var nextRetentionSweep = clock.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushAsync(stoppingToken);

                if (clock.UtcNow >= nextRetentionSweep)
                {
                    await PurgeAsync(stoppingToken);
                    nextRetentionSweep = clock.UtcNow.Add(RetentionInterval);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Instance log persistence failed; records in this batch were lost.");
            }

            try
            {
                await Task.Delay(FlushInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Best-effort final drain so the last records before a graceful shutdown are not lost. Uses
        // CancellationToken.None because stoppingToken is already cancelled by this point.
        try
        {
            await FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Final instance log flush failed during shutdown.");
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var batch = new List<InstanceLogEntry>(MaxBatchSize);
        while (batch.Count < MaxBatchSize && provider.Reader.TryRead(out var entry))
        {
            batch.Add(entry);
        }

        if (batch.Count == 0)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // One round trip for the whole batch. NpgsqlBatch keeps every value parameterized — building
        // an INSERT string from log messages would be the one place in this codebase where attacker
        // -influenced text reaches SQL as text.
        await using var dbBatch = new NpgsqlBatch(connection);
        foreach (var entry in batch)
        {
            var command = new NpgsqlBatchCommand(
                """
                INSERT INTO platform.instance_logs
                    (id, created_at_utc, level, category, message, exception, correlation_id, user_id, workspace_id)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                """);

            command.Parameters.Add(new NpgsqlParameter { Value = entry.Id });
            command.Parameters.Add(new NpgsqlParameter { Value = entry.CreatedAtUtc });
            command.Parameters.Add(new NpgsqlParameter { Value = entry.Level });
            command.Parameters.Add(new NpgsqlParameter { Value = entry.Category });
            command.Parameters.Add(new NpgsqlParameter { Value = entry.Message });
            command.Parameters.Add(Nullable(entry.Exception, NpgsqlDbType.Text));
            command.Parameters.Add(Nullable(entry.CorrelationId, NpgsqlDbType.Varchar));
            command.Parameters.Add(Nullable(entry.UserId, NpgsqlDbType.Uuid));
            command.Parameters.Add(Nullable(entry.WorkspaceId, NpgsqlDbType.Uuid));

            dbBatch.BatchCommands.Add(command);
        }

        await dbBatch.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        var retentionDays = Math.Max(1, provider.Options.RetentionDays);
        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "DELETE FROM platform.instance_logs WHERE created_at_utc < $1", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = cutoff });

        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation(
                "Purged {Count} instance log record(s) older than {RetentionDays} day(s).", deleted, retentionDays);
        }
    }

    /// <summary>An explicitly typed parameter, because Npgsql cannot infer a type from a null value.</summary>
    private static NpgsqlParameter Nullable(object? value, NpgsqlDbType type)
        => new() { Value = value ?? DBNull.Value, NpgsqlDbType = type };
}
