namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Tenancy.Authorization;

/// <summary>
/// Read-only role/permission listing for the workspace roles admin screen (ADR-0003). Full
/// custom-role CRUD lands later; the data model already supports a non-built-in role.
/// </summary>
public sealed class RoleService(
    IWorkspaceContextAccessor workspaceAccessor,
    IRoleStore roles,
    IMembershipStore memberships,
    IWorkspaceStore workspaces,
    IRolePermissionResolver roleResolver)
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var ctx = workspaceAccessor.Current;
        if (!ctx.HasWorkspace)
        {
            throw new ForbiddenException("A workspace context is required for this operation.");
        }

        var workspace = await workspaces.FindByIdAsync(workspaceId, cancellationToken)
            ?? throw new NotFoundException("Workspace not found.");

        var caller = await memberships.FindAsync(workspace.Id, ctx.UserId, cancellationToken);
        var permissions = await roleResolver.ResolveAsync(caller, cancellationToken);
        TenancyAuthorizer.Ensure(permissions, TenancyPermissions.MembersView);

        var withPermissions = await roles.ListWithPermissionsAsync(workspace.Id, cancellationToken);
        return withPermissions
            .Select(r => new RoleDto(r.Role.Id, r.Role.Key, r.Role.Name, r.Role.IsBuiltIn, r.Permissions))
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }
}
