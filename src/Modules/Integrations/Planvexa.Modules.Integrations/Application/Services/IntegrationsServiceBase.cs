namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Shared dependencies for Integrations application services.</summary>
public sealed class IntegrationsServiceContext(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    IIdGenerator ids,
    IClock clock,
    IAuditWriter audit,
    IWorkspaceAccessQuery access,
    IUnitOfWork unitOfWork)
{
    public IWorkspaceContextAccessor WorkspaceAccessor => workspaceAccessor;
    public ICurrentUser CurrentUser => currentUser;
    public IIdGenerator Ids => ids;
    public IClock Clock => clock;
    public IAuditWriter Audit => audit;
    public IWorkspaceAccessQuery Access => access;
    public IUnitOfWork UnitOfWork => unitOfWork;
}

public abstract class IntegrationsServiceBase(IntegrationsServiceContext ctx)
{
    protected IntegrationsServiceContext Ctx => ctx;
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

    protected async Task<WorkspaceAccess?> AccessAsync(Guid workspaceId, CancellationToken ct)
        => await ctx.Access.GetAccessAsync(workspaceId, UserId, ct);

    protected void Audit(string action, string entityType, Guid? entityId, object? data = null)
        => ctx.Audit.Write(action, entityType, entityId, data);

    protected Task SaveAsync(CancellationToken ct) => ctx.UnitOfWork.SaveChangesAsync(ct);
}
