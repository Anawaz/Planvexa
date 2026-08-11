namespace Planvexa.Modules.Collaboration.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.SharedContracts.Search;
using Planvexa.SharedContracts.Work;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Cross-module search: body matches over this workspace's comments. Comments have no privacy of
/// their own — visibility is entirely inherited from the task they are posted on (a private task's
/// comments must be exactly as hidden as the task itself). Collaboration does not own Task privacy/ACL
/// data (AGENTS.md rule 7 — no direct cross-module table reads), so this filters each candidate through
/// the shared <see cref="IResourcePermissionQuery"/> resolver against the comment's owning Task, the same
/// authoritative ACL/privacy resolver WorkManagement's own authorizer is built on (ADR-0003) —
/// see ISearchProvider's doc comment on why this filter is not optional.
/// </summary>
public sealed class CommentSearchProvider(
    IWorkspaceContextAccessor workspaceAccessor,
    ICurrentUser currentUser,
    ICommentStore comments,
    ITaskDirectory tasks,
    IWorkspaceAccessQuery access,
    IResourcePermissionQuery acl) : ISearchProvider
{
    private const string TaskResourceType = "task";

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string term, int limit, CancellationToken cancellationToken = default)
    {
        var workspace = workspaceAccessor.Current;
        if (!workspace.HasWorkspace)
        {
            return [];
        }

        var workspaceAccess = await access.GetAccessAsync(workspace.WorkspaceId, currentUser.UserId, cancellationToken);
        if (workspaceAccess is null)
        {
            return [];
        }

        var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var contains = $"%{escaped}%";
        var candidates = await comments.SearchByWorkspaceAsync(workspace.WorkspaceId, contains, limit * 3, cancellationToken);

        var hits = new List<SearchHit>();
        foreach (var comment in candidates)
        {
            if (hits.Count >= limit)
            {
                break;
            }

            var task = await tasks.FindAsync(comment.TaskId, cancellationToken);
            if (task is null || task.WorkspaceId != workspace.WorkspaceId)
            {
                continue;
            }

            var level = await acl.GetEffectiveAsync(workspace.WorkspaceId, currentUser.UserId, TaskResourceType, task.TaskId, cancellationToken);
            if (level is null || level < PermissionLevel.View)
            {
                continue;
            }

            hits.Add(new SearchHit("Comment", task.TaskId, Snippet(comment.Body), $"Comment on \"{task.Title}\"", task.ListId));
        }

        return hits;
    }

    private static string Snippet(string body) => body.Length <= 120 ? body : body[..120];
}
