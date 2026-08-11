namespace Planvexa.Modules.Tenancy.Authorization;

using System.Collections.Concurrent;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Domain;

/// <summary>Resolves the effective permission set for a workspace member (ADR-0003).</summary>
public interface IRolePermissionResolver
{
    Task<IReadOnlySet<string>> ResolveAsync(WorkspaceMember? member, CancellationToken cancellationToken = default);
}

/// <summary>
/// RoleId (when set) is authoritative and is read from tenancy.role_permissions via
/// <see cref="IRoleStore"/>, cached briefly per role so a permission check costs at most one query per
/// role per cache window rather than one per authorization check (AGENTS.md: keep authorization checks
/// fast — no N+1). A null RoleId (pre-backfill member, or the fast compatibility path) falls back to
/// the static <see cref="RolePermissions"/> switch keyed by the MembershipRole enum, no DB access.
/// </summary>
public sealed class RolePermissionResolver(IRoleStore roles) : IRolePermissionResolver
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    // ponytail: static, process-local cache with no cross-instance invalidation. Safe while built-in
    // role grants are effectively read-only (this change has no role-editing UI); switch to a proper
    // invalidated cache (or IMemoryCache decorated with a change token) once custom-role CRUD ships.
    private static readonly ConcurrentDictionary<Guid, (IReadOnlySet<string> Permissions, DateTimeOffset ExpiresAtUtc)> Cache = new();

    public async Task<IReadOnlySet<string>> ResolveAsync(WorkspaceMember? member, CancellationToken cancellationToken = default)
    {
        if (member is null)
        {
            return RolePermissions.None;
        }

        if (member.RoleId is not { } roleId)
        {
            return RolePermissions.For(member.Role);
        }

        if (Cache.TryGetValue(roleId, out var cached) && cached.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cached.Permissions;
        }

        var permissions = await roles.GetPermissionKeysAsync(roleId, cancellationToken);
        Cache[roleId] = (permissions, DateTimeOffset.UtcNow.Add(CacheDuration));
        return permissions;
    }
}
