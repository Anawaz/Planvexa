namespace Planvexa.Api.Notifications;

using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.TimeTracking.Application.Services;

/// <summary>
/// Sends a "you haven't logged enough time" reminder on the cadence each workspace configures on its
/// TimePolicy. Mirrors <see cref="DigestBackgroundService"/>'s exact shape: periodically
/// lists every workspace's policy via <see cref="MissingTimeReminderRunner.ListEnabledAsync"/>, and for
/// each opens a scope, binds the workspace context (system actor), and runs
/// <see cref="MissingTimeReminderRunner.RunAsync"/>, which is idempotent (see its doc comment) so a
/// poll that finds nothing newly due is a no-op.
/// </summary>
public sealed class MissingTimeReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<MissingTimeReminderBackgroundService> logger) : BackgroundService
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
                logger.LogError(ex, "Missing-time reminder loop failed; will retry.");
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
        var now = DateTimeOffset.UtcNow;

        List<Guid> workspaceIds;
        using (var listScope = scopeFactory.CreateScope().UseMaintenanceConnection())
        {
            var runner = listScope.ServiceProvider.GetRequiredService<MissingTimeReminderRunner>();
            var policies = await runner.ListEnabledAsync(ct);
            workspaceIds = policies.Select(p => p.WorkspaceId).ToList();
        }

        foreach (var workspaceId in workspaceIds)
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

            var policyStore = scope.ServiceProvider.GetRequiredService<Modules.TimeTracking.Application.ITimePolicyStore>();
            var policy = await policyStore.FindAsync(workspaceId, ct);
            if (policy is null || !policy.MissingTimeReminderEnabled)
            {
                continue;
            }

            var runner = scope.ServiceProvider.GetRequiredService<MissingTimeReminderRunner>();
            var sent = await runner.RunAsync(policy, now, ct);
            if (sent > 0)
            {
                logger.LogInformation(
                    "Sent {Count} missing-time reminders for workspace {WorkspaceId}", sent, workspaceId);
            }
        }
    }
}
