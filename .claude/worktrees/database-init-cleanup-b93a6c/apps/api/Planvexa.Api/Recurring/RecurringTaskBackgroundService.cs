namespace Planvexa.Api.Recurring;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Application.Services;

/// <summary>
/// Periodically generates due recurring-task occurrences. For each due definition it establishes the
/// definition's Workspace context (so RLS and query filters bind correctly) and calls the idempotent
/// generator. Duplicate generation is impossible thanks to the occurrence dedup key (ADR-0009).
/// </summary>
public sealed class RecurringTaskBackgroundService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<RecurringTaskBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small initial delay so the app finishes starting (and migrations complete) first.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Recurring generation loop failed; will retry.");
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

    private async Task ProcessDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;

        using var listScope = scopeFactory.CreateScope().UseMaintenanceConnection();
        var store = listScope.ServiceProvider.GetRequiredService<IRecurringTaskStore>();
        var due = await store.ListDueAsync(now, BatchSize, ct);

        foreach (var definition in due)
        {
            using var scope = scopeFactory.CreateScope().UseMaintenanceConnection();

            // Bind the ambient Workspace to the definition's workspace for correct isolation.
            var accessor = scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>();
            accessor.Set(new WorkspaceContext(
                workspaceId: definition.WorkspaceId,
                userId: definition.CreatedByUserId,
                membershipId: null,
                role: string.Empty,
                permissions: new HashSet<string>(),
                entitlements: new HashSet<string>(),
                correlationId: Guid.CreateVersion7().ToString()));

            var service = scope.ServiceProvider.GetRequiredService<RecurringTaskService>();
            var reloadStore = scope.ServiceProvider.GetRequiredService<IRecurringTaskStore>();
            var scoped = await reloadStore.FindAsync(definition.Id, ct);
            if (scoped is null)
            {
                continue;
            }

            try
            {
                var result = await service.GenerateAsync(scoped, now, ct);
                if (result.Generated)
                {
                    logger.LogInformation("Generated recurring task {TaskId} for definition {DefinitionId}", result.TaskId, definition.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to generate occurrence for recurring definition {DefinitionId}", definition.Id);
            }
        }
    }
}
