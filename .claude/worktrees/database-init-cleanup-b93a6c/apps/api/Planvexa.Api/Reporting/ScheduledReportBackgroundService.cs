namespace Planvexa.Api.Reporting;

using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Reporting.Application.Services;

/// <summary>
/// Sends due scheduled-report exports on a schedule. Periodically lists every enabled
/// ScheduledReport across workspaces, and for each opens a scope, binds the ambient Workspace (system
/// actor), and runs it via <see cref="ScheduledReportRunner"/> when due. Mirrors
/// <see cref="Planvexa.Api.Notifications.DigestBackgroundService"/>'s exact shape. Idempotent:
/// <see cref="ScheduledReportRunner.RunAsync"/> only sends when
/// <see cref="Planvexa.Modules.Reporting.Domain.ScheduledReport.IsDue"/> and always advances
/// <c>LastSentAtUtc</c>, so a poll that finds nothing due is a no-op.
/// </summary>
public sealed class ScheduledReportBackgroundService(
    IServiceScopeFactory scopeFactory, ILogger<ScheduledReportBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
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
                logger.LogError(ex, "Scheduled report processing loop failed; will retry.");
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
        List<(Guid Id, Guid WorkspaceId)> due;
        using (var listScope = scopeFactory.CreateScope().UseMaintenanceConnection())
        {
            var runner = listScope.ServiceProvider.GetRequiredService<ScheduledReportRunner>();
            var reports = await runner.ListEnabledAsync(ct);
            due = reports.Where(r => r.IsDue(DateTimeOffset.UtcNow)).Select(r => (r.Id, r.WorkspaceId)).ToList();
        }

        foreach (var (id, workspaceId) in due)
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

            var store = scope.ServiceProvider.GetRequiredService<Modules.Reporting.Application.IScheduledReportStore>();
            var report = await store.FindAsync(workspaceId, id, ct);
            if (report is null || !report.IsDue(DateTimeOffset.UtcNow))
            {
                continue;
            }

            var runner = scope.ServiceProvider.GetRequiredService<ScheduledReportRunner>();
            var sent = await runner.RunAsync(report, ct);
            if (sent)
            {
                logger.LogInformation("Sent scheduled report {ReportId} for workspace {WorkspaceId}", id, workspaceId);
            }
        }
    }
}
