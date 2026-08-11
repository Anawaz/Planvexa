namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.WorkManagement.Application;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Shared dependencies + helpers for WorkManagement services: ambient workspace/user, workspace access
/// resolution, audit + task-activity writing, and id/clock access.
/// </summary>
public abstract class WorkServiceBase(WorkServiceContext ctx)
{
    protected WorkServiceContext Ctx => ctx;
    protected Guid UserId => ctx.CurrentUser.UserId;
    protected DateTimeOffset Now => ctx.Clock.UtcNow;

    protected Guid RequireWorkspace()
    {
        var workspace = ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            throw new ForbiddenException("An X-Workspace header identifying the target workspace is required.");
        }

        return workspace.WorkspaceId;
    }

    protected Guid NewId() => ctx.Ids.NewId();

    protected Task<WorkspaceAccess?> AccessAsync(Guid workspaceId, CancellationToken ct)
        => ctx.Access.GetAccessAsync(workspaceId, UserId, ct);

    /// <summary>ADR-0003: read-gates a resource, honoring private flags (own + ancestor) and ACL grants (see WorkManagementAuthorizer).</summary>
    protected async Task EnsureReadAsync(WorkEntity resource, string resourceType, CancellationToken ct)
    {
        var role = (await AccessAsync(resource.WorkspaceId, ct))?.Role;
        await WorkManagementAuthorizer.EnsureReadAsync(resource, role, UserId, resourceType, ctx.ResourcePermissions, ctx.Hierarchy, ct);
    }

    protected async Task EnsureEditContentAsync(WorkEntity resource, string resourceType, CancellationToken ct)
    {
        var role = (await AccessAsync(resource.WorkspaceId, ct))?.Role;
        await WorkManagementAuthorizer.EnsureEditContentAsync(resource, role, UserId, resourceType, ctx.ResourcePermissions, ctx.Hierarchy, ct);
    }

    protected async Task EnsureManageStructureAsync(WorkEntity resource, string resourceType, CancellationToken ct)
    {
        var role = (await AccessAsync(resource.WorkspaceId, ct))?.Role;
        await WorkManagementAuthorizer.EnsureManageStructureAsync(resource, role, UserId, resourceType, ctx.ResourcePermissions, ctx.Hierarchy, ct);
    }

    /// <summary>Non-throwing read check, for filtering a listing down to what the caller may see.</summary>
    protected async Task<bool> CanReadAsync(WorkEntity resource, string resourceType, CancellationToken ct)
    {
        var role = (await AccessAsync(resource.WorkspaceId, ct))?.Role;
        return await WorkManagementAuthorizer.CanReadAsync(resource, role, UserId, resourceType, ctx.ResourcePermissions, ctx.Hierarchy, ct);
    }

    /// <summary>Non-throwing edit check, used by bulk operations to skip items the caller may not edit.</summary>
    protected async Task<bool> CanEditContentAsync(WorkEntity resource, string resourceType, CancellationToken ct)
    {
        var role = (await AccessAsync(resource.WorkspaceId, ct))?.Role;
        return await WorkManagementAuthorizer.CanEditContentAsync(resource, role, UserId, resourceType, ctx.ResourcePermissions, ctx.Hierarchy, ct);
    }

    /// <summary>Non-throwing read check for a Task evaluated through a SPECIFIC List membership
    /// (not its primary list) — see WorkManagementAuthorizer.EnsureReadInListContextAsync's doc comment.</summary>
    protected async Task<bool> CanReadInListContextAsync(WorkItem task, Guid viaListId, CancellationToken ct)
    {
        var role = (await AccessAsync(task.WorkspaceId, ct))?.Role;
        return await WorkManagementAuthorizer.CanReadInListContextAsync(task, viaListId, role, UserId, ctx.ResourcePermissions, ctx.Hierarchy, ct);
    }

    /// <summary>Bulk form of <see cref="CanReadInListContextAsync"/> for filtering a whole list's tasks
    /// in O(1) DB round trips for the common case (see WorkManagementAuthorizer.FilterReadableInListContextAsync).</summary>
    protected async Task<IReadOnlyList<WorkItem>> FilterReadableInListContextAsync(
        IReadOnlyList<WorkItem> candidates, Guid workspaceId, Guid viaListId, CancellationToken ct)
    {
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        return await WorkManagementAuthorizer.FilterReadableInListContextAsync(
            candidates, viaListId, workspaceId, role, UserId, ctx.ResourcePermissions, ctx.Hierarchy, ct);
    }

    protected void Audit(string action, string entityType, Guid? entityId, object? data = null)
        => ctx.Audit.Write(action, entityType, entityId, data);

    protected void Activity(Guid workspaceId, Guid taskId, string type, string? data = null)
        => ctx.ActivityStore.Add(new TaskActivityEvent(NewId(), workspaceId, taskId, UserId, type, data, Now));

    protected Task SaveAsync(CancellationToken ct) => ctx.UnitOfWork.SaveChangesAsync(ct);

    /// <summary>Broadcasts a realtime change signal for a task (best-effort; DB stays authoritative).</summary>
    protected Task NotifyRealtimeAsync(Guid workspaceId, Guid taskId, string action, CancellationToken ct)
        => ctx.Realtime.NotifyAsync(
            new RealtimeEvent(workspaceId, "Task", taskId, action, null, ctx.WorkspaceAccessor.Current.CorrelationId), ct);
}

/// <summary>Bundle of shared services injected into every WorkManagement application service.</summary>
public sealed class WorkServiceContext(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    IIdGenerator ids,
    IClock clock,
    IAuditWriter audit,
    IWorkspaceAccessQuery access,
    IResourcePermissionQuery resourcePermissions,
    WorkResourceHierarchyQuery hierarchy,
    IActivityStore activityStore,
    IRealtimeNotifier realtime,
    Planvexa.BuildingBlocks.Domain.IUnitOfWork unitOfWork)
{
    public IWorkspaceContextAccessor WorkspaceAccessor => workspaceAccessor;
    public ICurrentUser CurrentUser => currentUser;
    public IIdGenerator Ids => ids;
    public IClock Clock => clock;
    public IAuditWriter Audit => audit;
    public IWorkspaceAccessQuery Access => access;
    public IResourcePermissionQuery ResourcePermissions => resourcePermissions;

    /// <summary>
    /// Bound to the concrete WorkManagement implementation, not the shared <see cref="IResourceHierarchyQuery"/>
    /// interface — later modules will register their own implementations of that interface for Tenancy's
    /// resolver to enumerate, and a plain single-instance injection here would silently resolve to
    /// whichever one DI registered last instead of always this module's own.
    /// </summary>
    public WorkResourceHierarchyQuery Hierarchy => hierarchy;

    public IActivityStore ActivityStore => activityStore;
    public IRealtimeNotifier Realtime => realtime;
    public Planvexa.BuildingBlocks.Domain.IUnitOfWork UnitOfWork => unitOfWork;
}
