namespace Planvexa.Modules.TimeTracking.Domain.Events;

using Planvexa.SharedContracts.IntegrationEvents;

/// <summary>A time entry was logged against a task — either a running timer was stopped or a
/// manual entry was created. Only raised when the entry is linked to a task (TaskId is not null); an
/// entry with no task has nothing for an automation trigger to act on.</summary>
public sealed record TimeEntryLoggedIntegrationEvent(
    Guid WorkspaceId, Guid TaskId, Guid EntryId, Guid UserId, long DurationSeconds) : IntegrationEvent;
