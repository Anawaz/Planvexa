namespace Planvexa.Api.Outbox;

using Microsoft.EntityFrameworkCore;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.Infrastructure.Persistence;

/// <summary>
/// Drains the transactional outbox: fetches unprocessed messages, publishes them, and marks them
/// processed (ADR-0005). Idempotency is preserved because each message has a stable id and is only
/// published once processed-at is null. Runs with no ambient tenant.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IIntegrationEventPublisher publisher,
    IClock clock,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    /// <summary>A message that has failed this many times is parked: it stops being retried so one
    /// permanently broken payload cannot starve the rest of the batch forever.</summary>
    private const int MaxAttempts = 10;

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
                logger.LogError(ex, "Outbox processing loop failed; will retry.");
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

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope().UseMaintenanceConnection();
        var db = scope.ServiceProvider.GetRequiredService<PlanvexaDbContext>();

        // ponytail: parked messages stay in the table; add a dead-letter sweep when outbox volume matters.
        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                message.ProcessedOnUtc = clock.UtcNow;
                message.Error = null;
            }
            catch (Exception ex)
            {
                message.Attempts += 1;
                message.Error = ex.Message;
                if (message.Attempts >= MaxAttempts)
                {
                    logger.LogWarning(
                        ex,
                        "Outbox message {MessageId} parked after {Attempts} failed attempts; it will not be retried.",
                        message.Id,
                        message.Attempts);
                }
                else
                {
                    logger.LogError(ex, "Failed to publish outbox message {MessageId} (attempt {Attempts}).", message.Id, message.Attempts);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
