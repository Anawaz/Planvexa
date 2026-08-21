namespace Planvexa.Api.Platform;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.Infrastructure.Persistence;
using Planvexa.SharedContracts.Platform;

public sealed record InstanceHealth(
    bool DatabaseReachable,
    string? DatabaseVersion,
    int AppliedScripts,
    string? LatestScript,
    int OutboxPending,
    int OutboxFailed,
    int ErrorsLast24Hours,
    int WarningsLast24Hours,
    int DroppedLogRecords,
    bool LogCaptureEnabled,
    string LogMinimumLevel,
    int LogRetentionDays,
    string FileStorageProvider,
    string EmailSender,
    bool MaintenanceConnectionConfigured,
    string? Version,
    string Environment,
    /// <summary>This instance's self-registration setting.</summary>
    bool SelfRegistrationEnabled,
    /// <summary>Whether Planvexa can manage the identity provider's own registration flag.</summary>
    bool IdentityProviderManageable,
    /// <summary>The identity provider's flag, or null when it cannot be determined.</summary>
    bool? IdentityProviderRegistrationAllowed,
    string? IdentityProviderDetail);

/// <summary>
/// The host console's "is this installation healthy?" snapshot. Everything here is cheap to compute on
/// demand — counts and configuration reads — so there is no cached state and no background collector.
///
/// Deliberately not a replacement for <c>/health/live</c> and <c>/health/ready</c>, which stay
/// anonymous, minimal and load-balancer-facing. This is host-admin-only and answers a different
/// question: not "should traffic be routed here?" but "what should the operator look at?".
///
/// ponytail: no per-background-service heartbeat — there is no heartbeat infrastructure in this
///  codebase today, and adding a table plus a write on every sweep to answer "is the recurring-task
///  worker alive?" is only worth it once that question is actually being asked. Outbox backlog and the
///  error count are the leading indicators in the meantime.
/// </summary>
public sealed class InstanceHealthService(
    PlanvexaDbContext db,
    InstanceLogProvider logs,
    IClock clock,
    IConfiguration configuration,
    IHostEnvironment environment,
    IInstanceSettingsProvider instanceSettings,
    IIdentityProviderRegistration identityProvider)
{
    public async Task<InstanceHealth> GetAsync(CancellationToken cancellationToken = default)
    {
        var reachable = await db.Database.CanConnectAsync(cancellationToken);

        // Every database read below is guarded by `reachable`: an unreachable database is precisely
        // when this endpoint is most needed, so it must still answer rather than throw.
        var databaseVersion = reachable ? await ScalarAsync<string>("SHOW server_version", cancellationToken) : null;

        var appliedScripts = 0;
        string? latestScript = null;
        var outboxPending = 0;
        var outboxFailed = 0;
        var errors = 0;
        var warnings = 0;

        if (reachable)
        {
            // DbUp's journal — the authoritative "which schema version is this?" answer.
            appliedScripts = await ScalarAsync<int>(
                "SELECT count(*)::int FROM platform.schema_versions", cancellationToken);
            latestScript = await ScalarAsync<string>(
                "SELECT scriptname FROM platform.schema_versions ORDER BY applied DESC LIMIT 1", cancellationToken);

            outboxPending = await db.OutboxMessages.CountAsync(m => m.ProcessedOnUtc == null, cancellationToken);
            outboxFailed = await db.OutboxMessages.CountAsync(m => m.Error != null, cancellationToken);

            var since = clock.UtcNow.AddHours(-24);
            errors = await db.InstanceLogs.CountAsync(
                e => e.CreatedAtUtc >= since && (e.Level == "Error" || e.Level == "Critical"), cancellationToken);
            warnings = await db.InstanceLogs.CountAsync(
                e => e.CreatedAtUtc >= since && e.Level == "Warning", cancellationToken);
        }

        var selfRegistration = (await instanceSettings.GetAsync(cancellationToken)).AllowSelfRegistration;
        var identityProviderState = await identityProvider.GetAsync(cancellationToken);

        return new InstanceHealth(
            DatabaseReachable: reachable,
            DatabaseVersion: databaseVersion,
            AppliedScripts: appliedScripts,
            LatestScript: latestScript,
            OutboxPending: outboxPending,
            OutboxFailed: outboxFailed,
            ErrorsLast24Hours: errors,
            WarningsLast24Hours: warnings,
            DroppedLogRecords: logs.Dropped,
            LogCaptureEnabled: logs.Options.Enabled,
            LogMinimumLevel: logs.Options.MinimumLevel.ToString(),
            LogRetentionDays: logs.Options.RetentionDays,
            FileStorageProvider: string.Equals(configuration["FileStorage:Provider"], "S3", StringComparison.OrdinalIgnoreCase)
                ? "S3"
                : "LocalDisk",
            EmailSender: string.IsNullOrWhiteSpace(configuration["Smtp:Host"]) ? "Logging (no SMTP host)" : "SMTP",
            // Surfaced because its absence silently degrades every cross-workspace background sweep
            // (see MaintenanceConnection) — a symptom that is otherwise invisible until something
            // quietly stops happening.
            MaintenanceConnectionConfigured: !string.IsNullOrWhiteSpace(
                configuration.GetConnectionString("PlanvexaMaintenance")),
            Version: Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Environment: environment.EnvironmentName,
            // Both halves of self-registration, side by side. Reported here and not only on the settings
            // page because "users cannot sign up" is a health question, and the answer is almost always
            // that these two disagree.
            SelfRegistrationEnabled: selfRegistration,
            IdentityProviderManageable: identityProviderState.Manageable,
            IdentityProviderRegistrationAllowed: identityProviderState.RegistrationAllowed,
            IdentityProviderDetail: identityProviderState.Detail);
    }

    private async Task<T?> ScalarAsync<T>(string sql, CancellationToken cancellationToken)
    {
        try
        {
            // Borrowed, never disposed here: the DbContext owns this connection and disposing it would
            // break every subsequent query in the same scope (including the counts above).
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is null or DBNull ? default : (T)Convert.ChangeType(result, typeof(T));
        }
        catch (Exception)
        {
            // A missing journal table (a database deployed by something other than DbUp) or a
            // permission gap must degrade one field, not fail the whole health report.
            return default;
        }
    }
}
