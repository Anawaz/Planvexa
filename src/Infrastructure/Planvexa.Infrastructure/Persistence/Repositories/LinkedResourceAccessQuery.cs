namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Documents.Domain;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Implements the cross-module <see cref="ILinkedResourceAccessQuery"/> for Whiteboards/Clips'
/// Task/Document linking. See the interface's doc comment for why Task and Document need two different
/// underlying checks (Task has a real ACL resolver; Document only has IsPrivate/owner).
/// </summary>
internal sealed class LinkedResourceAccessQuery(PlanvexaDbContext db, IResourcePermissionQuery resourcePermissions) : ILinkedResourceAccessQuery
{
    public async Task<bool> CanViewAsync(Guid workspaceId, Guid userId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
    {
        if (string.Equals(resourceType, LinkedResourceTypes.Task, StringComparison.Ordinal))
        {
            var level = await resourcePermissions.GetEffectiveAsync(
                workspaceId, userId, Modules.WorkManagement.Authorization.WorkResourceTypes.Task, resourceId, cancellationToken);
            return level is not null && level >= PermissionLevel.View;
        }

        if (string.Equals(resourceType, LinkedResourceTypes.Document, StringComparison.Ordinal))
        {
            var document = await db.Set<Document>().FirstOrDefaultAsync(d => d.Id == resourceId, cancellationToken);
            return document is not null && document.WorkspaceId == workspaceId && document.CanBeViewedBy(userId);
        }

        return false;
    }
}
