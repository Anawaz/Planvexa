namespace Planvexa.Api.Notifications;

using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Notifications.Application;

/// <summary>
/// Sends daily/weekly activity digests on a schedule. Periodically lists every user's digest preference
/// across workspaces, and for each opens a scope, binds the workspace context (system actor), and
/// compiles + sends via <see cref="DigestRunner"/> when due. Mirrors
/// <see cref="Planvexa.Api.Governance.RetentionBackgroundService"/>'s exact shape. Idempotent:
/// <see cref="DigestRunner.RunAsync"/> only sends when <see cref="Planvexa.Modules.Notifications.Domain.DigestPreference.IsDue"/>
/// and always advances the bookkeeping timestamp, so a poll that finds nothing due is a no-op.
///
/// Poll interval is short relative to the daily/weekly cadence itself (the cadence is enforced by
/// <c>DigestPreference.IsDue</c>, not by this loop's timing) so a preference set moments ago, or a
/// digest that becomes due mid-day, is picked up promptly rather than waiting for a coarse fixed clock
/// tick — the same reasoning as the retention/export workers' polling loops.
/// </summary>
public sealed class DigestBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DigestBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Digest processing loop failed; will retry.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        List<(Guid WorkspaceId, Guid UserId)> due;
        using (var listScope = scopeFactory.CreateScope().UseMaintenanceConnection())
        {
            var runner = listScope.ServiceProvider.GetRequiredService<DigestRunner>();
            var preferences = await runner.ListEnabledAsync(ct);
            due = preferences.Where(p => p.IsDue(DateTimeOffset.UtcNow)).Select(p => (p.WorkspaceId, p.UserId)).ToList();
        }

        foreach (var (workspaceId, userId) in due)
        {
            using var scope = scopeFactory.CreateScope().UseMaintenanceConnection();

            var accessor = scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>();
            accessor.Set(new WorkspaceContext(
                workspaceId: workspaceId,
                userId: PlatformActors.System,
                membershipId: null,
                role: string.Empty,
                permissions: new HashSet<string>(),
                entitlements: new HashSet<string>(),
                correlationId: Guid.CreateVersion7().ToString()));

            var digestPreferences = scope.ServiceProvider.GetRequiredService<Modules.Notifications.Application.IDigestPreferenceStore>();
            var preference = await digestPreferences.FindAsync(workspaceId, userId, ct);
            if (preference is null || !preference.IsDue(DateTimeOffset.UtcNow))
            {
                continue;
            }

            var runner = scope.ServiceProvider.GetRequiredService<DigestRunner>();
            var itemCount = await runner.RunAsync(preference, ct);
            if (itemCount > 0)
            {
                logger.LogInformation("Sent a {Frequency} digest ({Count} items) to user {UserId} in workspace {WorkspaceId}",
                    preference.Frequency, itemCount, userId, workspaceId);
            }
        }
    }
}
