namespace Planvexa.SharedContracts.UserData;

/// <summary>
/// A Workspace the user belongs to, for the GDPR-style export and for the sole-owner safety check on
/// deletion. <see cref="IsSoleActiveOwner"/> is true when this user is the ONLY active Owner of that
/// Workspace — deleting/anonymizing them would leave the Workspace ownerless, mirroring the same
/// invariant <c>MembershipService.LeaveAsync</c> already enforces for leaving a Workspace.
/// </summary>
public sealed record UserWorkspaceMembership(
    Guid WorkspaceId, string WorkspaceName, string Role, DateTimeOffset JoinedAtUtc, bool IsSoleActiveOwner);

/// <summary>A task (WorkItem) the user created or is currently assigned to.</summary>
public sealed record UserTaskRecord(
    Guid TaskId, Guid WorkspaceId, string Title, string Relationship, DateTimeOffset CreatedAtUtc, bool IsDeleted);

/// <summary>A comment the user authored.</summary>
public sealed record UserCommentRecord(
    Guid CommentId, Guid WorkspaceId, Guid TaskId, string Body, DateTimeOffset CreatedAtUtc, bool IsDeleted);

/// <summary>A time entry the user logged.</summary>
public sealed record UserTimeEntryRecord(
    Guid TimeEntryId, Guid WorkspaceId, Guid? TaskId, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc,
    long DurationSeconds, string? Description);

/// <summary>
/// Cross-module read of a single user's OWN data, for the GDPR-style export/deletion flow (self-service
/// only — every method takes the caller's own userId, never an arbitrary one). Implemented
/// in Infrastructure (owns the DbContext), so the Identity module never reaches into WorkManagement,
/// Collaboration, TimeTracking or Tenancy tables directly (AGENTS.md: a module must not read/write another
/// module's tables) — mirrors the established Governance IExportDataSource/IAuditQuery pattern. Every
/// method spans EVERY Workspace the user belongs to, which the normal request connection cannot do
/// (work.tasks/collab.comments/time.time_entries FORCE row-level security keyed on the single ambient
/// Workspace) — the implementation runs these on the maintenance connection instead, the same mechanism
/// TenancyStores/IntegrationStores already use for other legitimately cross-Workspace lookups.
/// </summary>
public interface IUserDataQuery
{
    Task<IReadOnlyList<UserWorkspaceMembership>> GetMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserTaskRecord>> GetTasksAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserCommentRecord>> GetCommentsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserTimeEntryRecord>> GetTimeEntriesAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cross-module hard-delete for the parts of a user's data that are safe to actually erase. Personal access tokens (Integrations module) are pure credentials owned by exactly one user with
/// nothing else referencing them, so — unlike tasks/comments/time entries — there is no referential
/// integrity reason to keep them around anonymized; they are simply removed. Implemented in Infrastructure
/// for the same module-boundary reason as <see cref="IUserDataQuery"/>.
/// </summary>
public interface IUserDataEraser
{
    /// <summary>Hard-deletes every personal access token owned by the user, across all Workspaces. Returns the count removed.</summary>
    Task<int> DeletePersonalAccessTokensAsync(Guid userId, CancellationToken cancellationToken = default);
}
