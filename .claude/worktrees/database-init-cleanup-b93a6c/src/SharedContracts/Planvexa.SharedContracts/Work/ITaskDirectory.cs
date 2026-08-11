namespace Planvexa.SharedContracts.Work;

/// <summary>Minimal cross-module reference to a task, exposed by the WorkManagement module.</summary>
/// <param name="ListName">
/// Human-readable name of the containing list, so other modules can label a task's grouping without
/// reaching into WorkManagement. Empty when the list cannot be resolved.
/// </param>
public sealed record TaskRef(
    Guid TaskId,
    Guid WorkspaceId,
    Guid SpaceId,
    Guid ListId,
    string ListName,
    string Title,
    bool IsCompleted);

/// <summary>Due-date automation trigger: a task whose due date falls in a queried window.</summary>
public sealed record DueTaskRef(Guid TaskId, Guid WorkspaceId, DateTimeOffset DueDate);

/// <summary>SLA automation trigger: an open (non-completed) task's current status and how long
/// it has been there. <see cref="StatusId"/> mirrors the "toStatusId" shape already used by the
/// task.status_changed trigger (a workspace-specific status scheme id, not a name).</summary>
public sealed record TaskStatusAgeRef(Guid TaskId, Guid StatusId, DateTimeOffset EnteredStatusAtUtc);

/// <summary>
/// Contract (implemented by WorkManagement) so other modules can resolve a task's workspace and basic
/// fields without depending on WorkManagement internals (AGENTS.md rule 7). Runs under the ambient
/// workspace; returns null when the task does not exist or is not in the current workspace.
/// </summary>
public interface ITaskDirectory
{
    Task<TaskRef?> FindAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Open (non-deleted, non-completed) tasks whose due date falls in
    /// [<paramref name="fromUtc"/>, <paramref name="toUtc"/>) — used by <c>DueDateBackgroundService</c>'s
    /// sweep. Runs under the ambient workspace (<paramref name="workspaceId"/> must match it).</summary>
    Task<IReadOnlyList<DueTaskRef>> ListDueBetweenAsync(Guid workspaceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>Every open (non-deleted, non-completed) task's current status id and the
    /// instant it entered that status (the most recent "status_changed" activity event, falling back to
    /// the task's creation time if it has never changed status) — used by <c>SlaBackgroundService</c>'s
    /// sweep to compute "minutes in current status". Runs under the ambient workspace.</summary>
    Task<IReadOnlyList<TaskStatusAgeRef>> ListOpenTaskStatusAgesAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
