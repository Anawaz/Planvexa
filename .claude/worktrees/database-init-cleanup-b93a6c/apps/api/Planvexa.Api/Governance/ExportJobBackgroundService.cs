namespace Planvexa.Api.Governance;

using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Governance.Application.Services;

/// <summary>
/// Processes pending governed export jobs. Periodically claims Pending jobs across all workspaces, and
/// for each opens a scope, binds the ambient Workspace to the job's workspace (so the data source +
/// store isolate correctly), and runs it to Completed/Failed. Idempotent: only Pending jobs are claimed
/// and the state transition is persisted, so a job is produced at most once.
/// </summary>
public sealed class ExportJobBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExportJobBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Export job processing loop failed; will retry.");
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

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        List<(Guid Id, Guid WorkspaceId)> pending;
        using (var claimScope = scopeFactory.CreateScope().UseMaintenanceConnection())
        {
            var runner = claimScope.ServiceProvider.GetRequiredService<ExportRunner>();
            var jobs = await runner.ClaimPendingAsync(BatchSize, ct);
            pending = jobs.Select(j => (j.Id, j.WorkspaceId)).ToList();
        }

        foreach (var (id, workspaceId) in pending)
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

            var store = scope.ServiceProvider.GetRequiredService<Modules.Governance.Application.IExportJobStore>();
            var job = await store.FindAsync(id, ct);
            if (job is null || job.Status != Modules.Governance.Domain.ExportJobStatus.Pending)
            {
                continue;
            }

            var runner = scope.ServiceProvider.GetRequiredService<ExportRunner>();
            await runner.RunAsync(job, ct);
            logger.LogInformation("Processed export job {JobId} for workspace {WorkspaceId}", id, workspaceId);
        }
    }
}
