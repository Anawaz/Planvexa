namespace Planvexa.BuildingBlocks.Workspaces;

/// <summary>Immutable implementation of <see cref="IWorkspaceContext"/>.</summary>
public sealed class WorkspaceContext : IWorkspaceContext
{
    public static readonly WorkspaceContext None = new();

    private WorkspaceContext()
    {
        HasWorkspace = false;
        Role = string.Empty;
        Permissions = new HashSet<string>();
        Entitlements = new HashSet<string>();
        CorrelationId = string.Empty;
    }

    public WorkspaceContext(
        Guid workspaceId,
        Guid userId,
        Guid? membershipId,
        string role,
        IReadOnlySet<string> permissions,
        IReadOnlySet<string> entitlements,
        string correlationId)
    {
        HasWorkspace = true;
        WorkspaceId = workspaceId;
        UserId = userId;
        MembershipId = membershipId;
        Role = role ?? string.Empty;
        Permissions = permissions ?? new HashSet<string>();
        Entitlements = entitlements ?? new HashSet<string>();
        CorrelationId = correlationId ?? string.Empty;
    }

    public bool HasWorkspace { get; }
    public Guid WorkspaceId { get; }
    public Guid UserId { get; }
    public Guid? MembershipId { get; }
    public string Role { get; }
    public IReadOnlySet<string> Permissions { get; }
    public IReadOnlySet<string> Entitlements { get; }
    public string CorrelationId { get; }
}

/// <summary>
/// Mutable holder resolved per request/scope. Middleware sets the context once; downstream code
/// reads <see cref="Current"/>.
/// </summary>
public interface IWorkspaceContextAccessor
{
    IWorkspaceContext Current { get; }
    void Set(IWorkspaceContext context);
}

public sealed class WorkspaceContextAccessor : IWorkspaceContextAccessor
{
    private IWorkspaceContext _current = WorkspaceContext.None;

    public IWorkspaceContext Current => _current;

    public void Set(IWorkspaceContext context) => _current = context ?? WorkspaceContext.None;
}
