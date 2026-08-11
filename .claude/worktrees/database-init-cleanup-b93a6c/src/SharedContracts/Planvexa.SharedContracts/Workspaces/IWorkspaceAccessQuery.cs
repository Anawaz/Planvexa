namespace Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Coarse workspace access level, shared across modules. Ordered so numeric comparison expresses
/// privilege (Owner is highest). Mirrors the Tenancy module's MembershipRole without leaking it.
/// </summary>
public enum WorkspaceRole
{
    Guest = 0,
    LimitedMember = 1,
    Member = 2,
    Admin = 3,
    Owner = 4,
}

/// <summary>The caller's resolved access to a specific workspace.</summary>
public sealed record WorkspaceAccess(Guid WorkspaceId, Guid UserId, WorkspaceRole Role, bool IsGuest);

/// <summary>
/// Cross-module contract (implemented by the Tenancy module) that lets other modules authorize
/// against workspace membership without depending on Tenancy internals (AGENTS.md rule 7).
/// Returns null when the user has no access to the workspace.
/// </summary>
public interface IWorkspaceAccessQuery
{
    Task<WorkspaceAccess?> GetAccessAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);
}
