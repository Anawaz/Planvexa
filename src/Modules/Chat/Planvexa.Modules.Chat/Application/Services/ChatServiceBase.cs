namespace Planvexa.Modules.Chat.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.SharedContracts.Notifications;
using Planvexa.SharedContracts.Workspaces;

/// <summary>Shared dependencies for Chat application services (includes the realtime notifier, the
/// cross-module resource-ACL resolver for linked channels, notifications for mentions, and file storage
/// for attachments).</summary>
public sealed class ChatServiceContext(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    IIdGenerator ids,
    IClock clock,
    IAuditWriter audit,
    IWorkspaceAccessQuery access,
    IResourcePermissionQuery resourcePermissions,
    INotificationPublisher notifications,
    IFileStorage fileStorage,
    IMalwareScanner malwareScanner,
    IRealtimeNotifier realtime,
    IUnitOfWork unitOfWork)
{
    public IWorkspaceContextAccessor WorkspaceAccessor => workspaceAccessor;
    public ICurrentUser CurrentUser => currentUser;
    public IIdGenerator Ids => ids;
    public IClock Clock => clock;
    public IAuditWriter Audit => audit;
    public IWorkspaceAccessQuery Access => access;
    public IResourcePermissionQuery ResourcePermissions => resourcePermissions;
    public INotificationPublisher Notifications => notifications;
    public IFileStorage FileStorage => fileStorage;
    public IMalwareScanner MalwareScanner => malwareScanner;
    public IRealtimeNotifier Realtime => realtime;
    public IUnitOfWork UnitOfWork => unitOfWork;
}

public abstract class ChatServiceBase(ChatServiceContext ctx)
{
    protected ChatServiceContext Ctx => ctx;
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

    /// <summary>Broadcasts a realtime chat signal to the workspace group (best-effort; DB stays authoritative).</summary>
    protected Task NotifyAsync(Guid workspaceId, string entityType, Guid entityId, string action, CancellationToken ct)
        => ctx.Realtime.NotifyAsync(
            new RealtimeEvent(workspaceId, entityType, entityId, action, null, ctx.WorkspaceAccessor.Current.CorrelationId), ct);
}
