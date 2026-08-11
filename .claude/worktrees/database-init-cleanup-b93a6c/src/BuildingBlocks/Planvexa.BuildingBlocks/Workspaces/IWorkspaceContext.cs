namespace Planvexa.BuildingBlocks.Workspaces;

/// <summary>
/// Immutable, server-resolved Workspace context for the current operation (ADR 0015).
/// NEVER constructed from a request body or an unvalidated header (AGENTS.md rule 5). Populated by
/// Workspace-resolution middleware from the authenticated principal + the user's validated
/// membership. Workspace is the single top-level business/authorization boundary.
/// </summary>
public interface IWorkspaceContext
{
    bool HasWorkspace { get; }
    Guid WorkspaceId { get; }
    Guid UserId { get; }
    Guid? MembershipId { get; }
    string Role { get; }
    IReadOnlySet<string> Permissions { get; }
    IReadOnlySet<string> Entitlements { get; }
    string CorrelationId { get; }
}
