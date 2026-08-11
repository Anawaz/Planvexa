namespace Planvexa.Api.Recurring;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Application.Services;

/// <summary>
/// Fires due task reminders. Scans for unsent reminders whose time has come (cross-workspace, on the
/// maintenance connection), then for each one binds the reminder's workspace context and publishes a
/// notification exactly once (dedup key <c>reminder:{id}</c>, plus the sent flag).
/// </summary>
public sealed class ReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12), stoppingToken);
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
                logger.LogError(ex, "Reminder dispatch loop failed; will retry.");
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
        var store = listScope.ServiceProvider.GetRequiredService<IReminderStore>();
        var due = await store.ListDueAsync(now, BatchSize, ct);

        foreach (var reminder in due)
        {
            using var scope = scopeFactory.CreateScope().UseMaintenanceConnection();

            var accessor = scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>();
            accessor.Set(new WorkspaceContext(
                workspaceId: reminder.WorkspaceId,
                userId: reminder.UserId,
                membershipId: null,
                role: string.Empty,
                permissions: new HashSet<string>(),
                entitlements: new HashSet<string>(),
                correlationId: Guid.CreateVersion7().ToString()));

            var reminders = scope.ServiceProvider.GetRequiredService<IReminderStore>();
            var scoped = await reminders.FindAsync(reminder.Id, ct);
            if (scoped is null)
            {
                continue;
            }

            try
            {
                var service = scope.ServiceProvider.GetRequiredService<ReminderService>();
                await service.DispatchAsync(scoped, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dispatch reminder {ReminderId}", reminder.Id);
            }
        }
    }
}
