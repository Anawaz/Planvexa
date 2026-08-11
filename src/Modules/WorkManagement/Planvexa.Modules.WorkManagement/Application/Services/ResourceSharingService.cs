namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Workspaces;

public sealed record GrantResourcePermissionCommand(string PrincipalType, Guid PrincipalId, PermissionLevel Level);

/// <summary>
/// ADR-0003: generic sharing/privacy endpoints over WorkManagement's four ACL resource types
/// (space/folder/list/task). Dispatches to the right store by resourceType, then delegates the actual
/// grant/revoke storage to Tenancy via <see cref="IResourcePermissionAdmin"/> (AGENTS.md rule 7 — this
/// module never touches tenancy.resource_permissions directly). Later modules with their own resource
/// types (Documents, Forms, Chat) would add an equivalent service for their own resources.
/// </summary>
public sealed class ResourceSharingService(
    WorkServiceContext ctx,
    ISpaceStore spaces, IFolderStore folders, ITaskListStore lists, IWorkItemStore tasks,
    IResourcePermissionAdmin aclAdmin) : WorkServiceBase(ctx)
{
    public async Task<IReadOnlyList<ResourcePermissionGrant>> ListAsync(string resourceType, Guid resourceId, CancellationToken ct = default)
    {
        var entity = await LoadAsync(resourceType, resourceId, ct);
        await EnsureManageOrShareAsync(entity, resourceType, ct);
        return await aclAdmin.ListForResourceAsync(entity.WorkspaceId, resourceType, resourceId, ct);
    }

    public async Task<ResourcePermissionGrant> GrantAsync(
        string resourceType, Guid resourceId, GrantResourcePermissionCommand command, CancellationToken ct = default)
    {
        var entity = await LoadAsync(resourceType, resourceId, ct);
        await EnsureManageOrShareAsync(entity, resourceType, ct);

        var grant = await aclAdmin.GrantAsync(
            entity.WorkspaceId, UserId, resourceType, resourceId, command.PrincipalType, command.PrincipalId, command.Level, ct);
        Audit("resource_permission.granted", resourceType, resourceId, new { command.PrincipalType, command.PrincipalId, command.Level });
        await SaveAsync(ct);
        return grant;
    }

    public async Task RevokeAsync(string resourceType, Guid resourceId, string principalType, Guid principalId, CancellationToken ct = default)
    {
        var entity = await LoadAsync(resourceType, resourceId, ct);
        await EnsureManageOrShareAsync(entity, resourceType, ct);

        await aclAdmin.RevokeAsync(entity.WorkspaceId, resourceType, resourceId, principalType, principalId, ct);
        Audit("resource_permission.revoked", resourceType, resourceId, new { principalType, principalId });
        await SaveAsync(ct);
    }

    public async Task<bool> SetPrivateAsync(string resourceType, Guid resourceId, bool isPrivate, CancellationToken ct = default)
    {
        var entity = await LoadAsync(resourceType, resourceId, ct);
        await EnsureManageStructureAsync(entity, resourceType, ct);

        entity.SetPrivate(isPrivate, UserId, Now);
        Audit(isPrivate ? "resource.made_private" : "resource.made_public", resourceType, resourceId);
        await SaveAsync(ct);
        return entity.IsPrivate;
    }

    private async Task<WorkEntity> LoadAsync(string resourceType, Guid resourceId, CancellationToken ct)
    {
        WorkEntity? entity = resourceType switch
        {
            WorkResourceTypes.Space => await spaces.FindAsync(resourceId, ct),
            WorkResourceTypes.Folder => await folders.FindAsync(resourceId, ct),
            WorkResourceTypes.List => await lists.FindAsync(resourceId, ct),
            WorkResourceTypes.Task => await tasks.FindAsync(resourceId, ct),
            _ => throw new ValidationAppException($"Unknown resource type '{resourceType}'."),
        };

        if (entity is null || entity.IsDeleted)
        {
            throw new NotFoundException($"{resourceType} not found.");
        }

        return entity;
    }

    /// <summary>Sharing (grant/revoke/list ACL) requires manage OR share on the resource; Admin+ always qualifies.</summary>
    private async Task EnsureManageOrShareAsync(WorkEntity entity, string resourceType, CancellationToken ct)
    {
        var role = (await AccessAsync(entity.WorkspaceId, ct))?.Role;
        if (WorkManagementAuthorizer.CanManageStructure(role))
        {
            return;
        }

        var level = await Ctx.ResourcePermissions.GetEffectiveAsync(entity.WorkspaceId, UserId, resourceType, entity.Id, ct);
        if (level is PermissionLevel.Manage or PermissionLevel.Share)
        {
            return;
        }

        throw new ForbiddenException("You do not have permission to manage sharing for this resource.");
    }
}
