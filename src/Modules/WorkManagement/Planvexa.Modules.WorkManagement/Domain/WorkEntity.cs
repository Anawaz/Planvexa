namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>Base for workspace-owned, soft-deletable, ordered work-management entities.</summary>
public abstract class WorkEntity : Entity, IWorkspaceOwned, ISoftDeletable
{
    protected WorkEntity()
    {
    }

    protected WorkEntity(Guid id)
        : base(id)
    {
    }

    public Guid WorkspaceId { get; protected set; }
    public double Position { get; protected set; }
    public bool IsArchived { get; protected set; }

    /// <summary>
    /// ADR-0003: when true, only ACL grants on this resource (or a closer private ancestor's
    /// grant) or its creator can see it — the coarse workspace-role floor no longer applies. See
    /// SharedContracts.Workspaces.IResourcePermissionQuery for the inheritance-walk semantics.
    /// </summary>
    public bool IsPrivate { get; protected set; }

    public bool IsDeleted { get; protected set; }
    public DateTimeOffset? DeletedAtUtc { get; protected set; }
    public Guid? DeletedByUserId { get; protected set; }

    public DateTimeOffset CreatedAtUtc { get; protected set; }
    public Guid? CreatedByUserId { get; protected set; }
    public DateTimeOffset? UpdatedAtUtc { get; protected set; }
    public Guid? UpdatedByUserId { get; protected set; }

    public void Reposition(double position) => Position = position;

    public void SetPrivate(bool isPrivate, Guid userId, DateTimeOffset nowUtc)
    {
        IsPrivate = isPrivate;
        Touch(userId, nowUtc);
    }

    public void Archive()
    {
        IsArchived = true;
    }

    public void Unarchive()
    {
        IsArchived = false;
    }

    public void SoftDelete(Guid userId, DateTimeOffset nowUtc)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAtUtc = nowUtc;
        DeletedByUserId = userId;
    }

    public void Restore()
    {
        IsDeleted = false;
        DeletedAtUtc = null;
        DeletedByUserId = null;
    }

    protected void Touch(Guid userId, DateTimeOffset nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedByUserId = userId;
    }
}
