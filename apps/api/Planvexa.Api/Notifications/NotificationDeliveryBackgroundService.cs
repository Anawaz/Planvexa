namespace Planvexa.Api.Notifications;

using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Notifications.Application;

/// <summary>
/// Drains pending notification deliveries on a timer. Runs with no ambient tenant; the processor
/// reads across tenants and dispatches each channel idempotently.
/// </summary>
public sealed class NotificationDeliveryBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationDeliveryBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope().UseMaintenanceConnection();
                var processor = scope.ServiceProvider.GetRequiredService<NotificationDeliveryProcessor>();
                await processor.ProcessPendingAsync(BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification delivery loop failed; will retry.");
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
}
