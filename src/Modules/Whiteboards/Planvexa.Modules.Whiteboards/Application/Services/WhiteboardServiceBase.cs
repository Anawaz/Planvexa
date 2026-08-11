namespace Planvexa.Modules.Whiteboards.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Shared dependencies for Whiteboards application services (mirrors DocumentsServiceContext,
/// plus the cross-module linked-resource ACL resolver Chat/Whiteboards/Clips all need, and file storage for
/// image elements dropped onto the canvas).</summary>
public sealed class WhiteboardServiceContext(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    IIdGenerator ids,
    IClock clock,
    IAuditWriter audit,
    IWorkspaceAccessQuery access,
    ILinkedResourceAccessQuery linkedResources,
    IFileStorage fileStorage,
    IMalwareScanner malwareScanner,
    IUnitOfWork unitOfWork)
{
    public IWorkspaceContextAccessor WorkspaceAccessor => workspaceAccessor;
    public ICurrentUser CurrentUser => currentUser;
    public IIdGenerator Ids => ids;
    public IClock Clock => clock;
    public IAuditWriter Audit => audit;
    public IWorkspaceAccessQuery Access => access;
    public ILinkedResourceAccessQuery LinkedResources => linkedResources;
    public IFileStorage FileStorage => fileStorage;
    public IMalwareScanner MalwareScanner => malwareScanner;
    public IUnitOfWork UnitOfWork => unitOfWork;
}

public abstract class WhiteboardServiceBase(WhiteboardServiceContext ctx)
{
    protected WhiteboardServiceContext Ctx => ctx;
    protected Guid UserId => ctx.CurrentUser.UserId;
    protected DateTimeOffset Now => ctx.Clock.UtcNow;
    protected Guid NewId() => ctx.Ids.NewId();

    protected Guid RequireWorkspace()
    {
        var workspace = ctx.WorkspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            throw new ForbiddenException("An X-Workspace header identifying the target workspace is required.");
        }

        return workspace.WorkspaceId;
    }

    protected async Task<WorkspaceRole?> RoleAsync(Guid workspaceId, CancellationToken ct)
        => (await ctx.Access.GetAccessAsync(workspaceId, UserId, ct))?.Role;

    protected void Audit(string action, string entityType, Guid? entityId, object? data = null)
        => ctx.Audit.Write(action, entityType, entityId, data);

    protected Task SaveAsync(CancellationToken ct) => ctx.UnitOfWork.SaveChangesAsync(ct);
}
