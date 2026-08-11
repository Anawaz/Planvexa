namespace Planvexa.SharedContracts.Collaboration;

/// <summary>
/// Write-side contract (implemented by Collaboration) that lets other modules post a comment on a task
/// without depending on Collaboration internals (AGENTS.md rule 7). Serves Automations' "comment" action
///, mirroring how Forms/Automations already create/mutate tasks via
/// <see cref="Planvexa.SharedContracts.Work.ITaskWriteApi"/>. Runs under the ambient workspace; the
/// target task is re-validated to belong to it. Posts always as an explicit author id (typically the
/// system actor for automation-posted comments) rather than the ambient <c>ICurrentUser</c>, since the
/// caller here IS the system, not an interactive request.
/// </summary>
public interface ICommentWriteApi
{
    /// <summary>Posts a top-level comment on the task. Returns the new comment id, or null if the task
    /// does not exist in the given workspace. No mentions are parsed/notified for system-authored
    /// comments (there is no interactive author to attribute a "you were mentioned" notification to).</summary>
    Task<Guid?> PostSystemCommentAsync(Guid workspaceId, Guid taskId, Guid authorUserId, string body, CancellationToken cancellationToken = default);
}
