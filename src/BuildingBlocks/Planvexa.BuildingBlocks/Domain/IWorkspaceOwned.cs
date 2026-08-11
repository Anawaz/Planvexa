namespace Planvexa.BuildingBlocks.Domain;

/// <summary>
/// Every Workspace-owned entity implements this. The persistence layer stamps and enforces
/// <see cref="WorkspaceId"/> and applies a global query filter. Workspace is the single top-level
/// business boundary (see ADR 0015). Global identity tables (Users, identity-provider links, truly
/// global user preferences/sessions) are NOT Workspace-owned and must not implement this interface.
/// </summary>
public interface IWorkspaceOwned
{
    Guid WorkspaceId { get; }
}

/// <summary>Entities that are never hard-deleted; a soft-delete flag preserves history.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAtUtc { get; }
    Guid? DeletedByUserId { get; }
}

/// <summary>Standard creation/modification audit stamps.</summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAtUtc { get; }
    Guid? CreatedByUserId { get; }
    DateTimeOffset? UpdatedAtUtc { get; }
    Guid? UpdatedByUserId { get; }
}
