namespace Planvexa.Api.Governance;

using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Governance.Application.Services;

/// <summary>
/// Applies workspace data-retention policies on a schedule. Periodically lists every workspace's policy,
/// and for each opens a scope, binds the workspace context (system actor), and purges expired
/// soft-deleted tasks via the Governance <see cref="RetentionRunner"/>. Legal hold or a keep-forever
/// window disables purging. Idempotent — only already-soft-deleted rows past the cutoff are removed, so
/// repeated runs converge.
/// </summary>
public sealed class RetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<RetentionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small initial delay so startup (and migrations) complete first.
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
                logger.LogError(ex, "Retention processing loop failed; will retry.");
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
        List<Guid> workspaceIds;
        using (var listScope = scopeFactory.CreateScope().UseMaintenanceConnection())
        {
            var runner = listScope.ServiceProvider.GetRequiredService<RetentionRunner>();
            var policies = await runner.ListPoliciesAsync(ct);
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

            var store = scope.ServiceProvider.GetRequiredService<Modules.Governance.Application.IRetentionPolicyStore>();
            var policy = await store.FindAsync(workspaceId, ct);
            if (policy is null)
            {
                continue;
            }

            var runner = scope.ServiceProvider.GetRequiredService<RetentionRunner>();
            var purged = await runner.ApplyAsync(policy, ct);
            if (purged > 0)
            {
                logger.LogInformation("Purged {Count} expired soft-deleted tasks for workspace {WorkspaceId}", purged, workspaceId);
            }
        }
    }
}
