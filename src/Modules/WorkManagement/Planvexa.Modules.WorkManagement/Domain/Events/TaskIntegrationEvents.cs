namespace Planvexa.Modules.WorkManagement.Domain.Events;

using Planvexa.SharedContracts.IntegrationEvents;

public sealed record TaskCreatedIntegrationEvent(
    Guid WorkspaceId, Guid ListId, Guid TaskId, string Title, Guid CreatedByUserId) : IntegrationEvent;

public sealed record TaskCompletedIntegrationEvent(
    Guid WorkspaceId, Guid TaskId, Guid CompletedByUserId) : IntegrationEvent;

public sealed record TaskStatusChangedIntegrationEvent(
    Guid WorkspaceId, Guid TaskId, Guid FromStatusId, Guid ToStatusId, Guid ChangedByUserId) : IntegrationEvent;

public sealed record TaskAssignedIntegrationEvent(
    Guid WorkspaceId, Guid TaskId, Guid AssigneeUserId, Guid AssignedByUserId) : IntegrationEvent;
