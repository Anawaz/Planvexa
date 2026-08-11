namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.SharedContracts.Workspaces;

public sealed class FeatureService(
    IWorkspaceContextAccessor workspaceAccessor,
    IFeatureEntitlementStore features,
    IWorkspaceAccessQuery access)
{
    /// <summary>
    /// Lists feature entitlements for a workspace. <paramref name="requestedWorkspaceId"/> is an
    /// explicit, client-suppliable id (query parameter) — it is never trusted directly; the caller
    /// must have active membership in it, verified the same way the ambient X-Workspace header would
    /// be. Falls back to the ambient workspace when no explicit id is given.
    /// </summary>
    public async Task<IReadOnlyList<FeatureDto>> ListAsync(Guid? requestedWorkspaceId, CancellationToken cancellationToken = default)
    {
        var ctx = workspaceAccessor.Current;
        if (!ctx.HasWorkspace)
        {
            throw new ForbiddenException("A workspace context is required for this operation.");
        }

        var workspaceId = requestedWorkspaceId ?? ctx.WorkspaceId;

        if (requestedWorkspaceId is not null && await access.GetAccessAsync(workspaceId, ctx.UserId, cancellationToken) is null)
        {
            throw new ForbiddenException("You do not have access to this workspace.");
        }

        var list = await features.ListByWorkspaceAsync(workspaceId, cancellationToken);
        return list
            .Select(f => new FeatureDto(f.FeatureKey, f.IsEnabled, f.Limit, f.Source))
            .ToList();
    }
}
