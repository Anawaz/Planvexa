namespace Planvexa.SharedContracts.Mobile;

/// <summary>A task change record for mobile delta sync.</summary>
public sealed record TaskChange(
    Guid TaskId, Guid ListId, Guid SpaceId, string Title, string Priority, bool IsCompleted,
    bool IsDeleted, DateTimeOffset? DueDate, DateTimeOffset ChangedAtUtc);

/// <summary>A page of changes plus the cursor to pass on the next sync call.</summary>
public sealed record ChangePage(IReadOnlyList<TaskChange> Changes, DateTimeOffset NextCursorUtc);

/// <summary>
/// Contract (implemented in Infrastructure) that returns tasks changed since a cursor for a workspace, for
/// mobile delta sync — without the Mobile module touching WorkManagement tables directly (AGENTS.md rule
/// 7). Runs under the ambient tenant; results are bounded and ordered by change time.
/// </summary>
public interface IChangeFeed
{
    Task<ChangePage> GetChangesAsync(Guid workspaceId, DateTimeOffset sinceUtc, int max, CancellationToken cancellationToken = default);
}
