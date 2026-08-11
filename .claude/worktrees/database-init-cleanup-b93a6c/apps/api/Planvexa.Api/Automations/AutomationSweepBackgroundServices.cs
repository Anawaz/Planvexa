namespace Planvexa.Api.Automations;

using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Automations.Application.Services;

/// <summary>
/// Background sweeps for the trigger types that aren't discrete events — due-date, scheduled
/// (cron-like), SLA breach, and bounded retry-with-backoff for Failed automation runs. All four mirror
/// <see cref="Planvexa.Api.Reporting.ScheduledReportBackgroundService"/>'s exact shape: list candidates
/// across every workspace under one short-lived "maintenance" scope (no ambient workspace — the store
/// query uses IgnoreQueryFilters), then process each candidate under its OWN new scope with the ambient
/// Workspace bound to it (system actor) — required because IAutomationDispatcher's internal queries are
/// workspace-scoped via the ambient context + RLS, exactly like WorkspaceEventDispatchingPublisher.
/// </summary>
public sealed class DueDateBackgroundService(IServiceScopeFactory scopeFactory, ILogger<DueDateBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await DelayStartupAsync(stoppingToken))
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
                logger.LogError(ex, "Due-date automation sweep failed; will retry.");
            }

            if (!await DelayAsync(PollInterval, stoppingToken))
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
            var runner = listScope.ServiceProvider.GetRequiredService<DueDateSweepRunner>();
            workspaceIds = (await runner.ListCandidateWorkspaceIdsAsync(ct)).ToList();
        }

        foreach (var workspaceId in workspaceIds)
        {
            using var scope = scopeFactory.CreateScope().UseMaintenanceConnection();
            BindSystemWorkspace(scope, workspaceId);
            var runner = scope.ServiceProvider.GetRequiredService<DueDateSweepRunner>();
            await runner.RunForWorkspaceAsync(workspaceId, ct);
        }
    }

    internal static void BindSystemWorkspace(IServiceScope scope, Guid workspaceId)
    {
        var accessor = scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>();
        accessor.Set(new WorkspaceContext(
            workspaceId: workspaceId,
            userId: PlatformActors.System,
            membershipId: null,
            role: string.Empty,
            permissions: new HashSet<string>(),
            entitlements: new HashSet<string>(),
            correlationId: Guid.CreateVersion7().ToString()));
    }

    internal static async Task<bool> DelayStartupAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

public sealed class ScheduledAutomationBackgroundService(IServiceScopeFactory scopeFactory, ILogger<ScheduledAutomationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await DueDateBackgroundService.DelayStartupAsync(stoppingToken))
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
                logger.LogError(ex, "Scheduled automation sweep failed; will retry.");
            }

            if (!await DueDateBackgroundService.DelayAsync(PollInterval, stoppingToken))
            {
                break;
            }
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        List<Planvexa.Modules.Automations.Domain.AutomationRule> due;
        using (var listScope = scopeFactory.CreateScope().UseMaintenanceConnection())
        {
            var runner = listScope.ServiceProvider.GetRequiredService<ScheduledAutomationSweepRunner>();
            due = (await runner.ListDueRulesAsync(ct)).ToList();
        }

        foreach (var rule in due)
        {
            using var scope = scopeFactory.CreateScope().UseMaintenanceConnection();
            DueDateBackgroundService.BindSystemWorkspace(scope, rule.WorkspaceId);
            var runner = scope.ServiceProvider.GetRequiredService<ScheduledAutomationSweepRunner>();
            await runner.RunForRuleAsync(rule, ct);
        }
    }
}

public sealed class SlaBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SlaBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await DueDateBackgroundService.DelayStartupAsync(stoppingToken))
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
                logger.LogError(ex, "SLA automation sweep failed; will retry.");
            }

            if (!await DueDateBackgroundService.DelayAsync(PollInterval, stoppingToken))
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
            var runner = listScope.ServiceProvider.GetRequiredService<SlaSweepRunner>();
            workspaceIds = (await runner.ListCandidateWorkspaceIdsAsync(ct)).ToList();
        }

        foreach (var workspaceId in workspaceIds)
        {
            using var scope = scopeFactory.CreateScope().UseMaintenanceConnection();
            DueDateBackgroundService.BindSystemWorkspace(scope, workspaceId);
            var runner = scope.ServiceProvider.GetRequiredService<SlaSweepRunner>();
            await runner.RunForWorkspaceAsync(workspaceId, ct);
        }
    }
}

/// <summary>Retries Failed automation runs with backoff (see AutomationRun.NextRetryAtUtc).
/// Polls frequently (backoff starts at ~2 minutes) since a retry becoming due is time-sensitive relative
/// to the other sweeps' daily/hourly cadence.</summary>
public sealed class AutomationRetryBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AutomationRetryBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                logger.LogError(ex, "Automation retry sweep failed; will retry.");
            }

            if (!await DueDateBackgroundService.DelayAsync(PollInterval, stoppingToken))
            {
                break;
            }
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        List<(Guid RunId, Guid WorkspaceId)> due;
        using (var listScope = scopeFactory.CreateScope().UseMaintenanceConnection())
        {
            var runner = listScope.ServiceProvider.GetRequiredService<AutomationRetryRunner>();
            due = (await runner.ListDueForRetryAsync(ct)).ToList();
        }

        foreach (var (runId, workspaceId) in due)
        {
            using var scope = scopeFactory.CreateScope().UseMaintenanceConnection();
            DueDateBackgroundService.BindSystemWorkspace(scope, workspaceId);
            var runner = scope.ServiceProvider.GetRequiredService<AutomationRetryRunner>();
            await runner.RetryOneAsync(runId, ct);
        }
    }
}
