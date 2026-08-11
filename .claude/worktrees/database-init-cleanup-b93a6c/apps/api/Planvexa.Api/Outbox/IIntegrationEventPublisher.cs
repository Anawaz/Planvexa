namespace Planvexa.Api.Outbox;

using Planvexa.BuildingBlocks.Outbox;

/// <summary>
/// Publishes integration events drained from the outbox. Today this is a logging publisher; a later
/// change replaces it with a NATS JetStream publisher without touching the outbox dispatcher.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed class LoggingIntegrationEventPublisher(ILogger<LoggingIntegrationEventPublisher> logger)
    : IIntegrationEventPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Publishing integration event {Type} ({EventId}) for workspace {WorkspaceId}",
            message.Type, message.Id, message.WorkspaceId);
        return Task.CompletedTask;
    }
}
