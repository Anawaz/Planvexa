namespace Planvexa.Modules.Tenancy.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A workspace-scoped role: one of the five built-in roles seeded into every workspace (owner, admin,
/// member, limited_member, guest — see Application/BuiltInRoles.cs) or a future custom role. Permission
/// grants live in <see cref="RolePermission"/> rows keyed by <see cref="Entity.Id"/> (ADR-0003).
/// </summary>
public sealed class Role : Entity, IWorkspaceOwned
{
    private Role()
    {
    }

    private Role(Guid id, Guid workspaceId, string key, string name, bool isBuiltIn, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Key = key;
        Name = name;
        IsBuiltIn = isBuiltIn;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsBuiltIn { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Role CreateBuiltIn(Guid id, Guid workspaceId, string key, string name, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstNullOrWhiteSpace(key, nameof(key));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new Role(id, workspaceId, key.Trim().ToLowerInvariant(), name.Trim(), isBuiltIn: true, nowUtc);
    }
}

/// <summary>
/// A permission grant on a <see cref="Role"/>. Composite key (RoleId, PermissionKey) — this is a pure
/// grant row, not an aggregate, so it does not derive <see cref="Entity"/>. WorkspaceId is denormalized
/// here (rather than requiring a join through Role) for direct RLS scoping and a workspace-led index,
/// matching the pattern used by other workspace-owned join tables (e.g. work.task_tags).
/// </summary>
public sealed class RolePermission : IWorkspaceOwned
{
    private RolePermission()
    {
    }

    private RolePermission(Guid workspaceId, Guid roleId, string permissionKey)
    {
        WorkspaceId = workspaceId;
        RoleId = roleId;
        PermissionKey = permissionKey;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid RoleId { get; private set; }
    public string PermissionKey { get; private set; } = string.Empty;

    public static RolePermission Grant(Guid workspaceId, Guid roleId, string permissionKey)
    {
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstEmpty(roleId, nameof(roleId));
        Guard.AgainstNullOrWhiteSpace(permissionKey, nameof(permissionKey));
        return new RolePermission(workspaceId, roleId, permissionKey);
    }
}
