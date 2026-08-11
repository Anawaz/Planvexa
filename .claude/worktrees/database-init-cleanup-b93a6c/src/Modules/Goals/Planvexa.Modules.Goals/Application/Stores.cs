namespace Planvexa.Modules.Goals.Application;

using Planvexa.Modules.Goals.Domain;

public interface IGoalStore
{
    void Add(Goal goal);
    void Remove(Goal goal);
    Task<Goal?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<Goal?> FindWithLinkedTasksAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Goal>> ListByWorkspaceAsync(Guid workspaceId, Guid? folderId, CancellationToken ct = default);
}

public interface IGoalFolderStore
{
    void Add(GoalFolder folder);
    void Remove(GoalFolder folder);
    Task<GoalFolder?> FindAsync(Guid workspaceId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<GoalFolder>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IGoalCommentStore
{
    void Add(GoalComment comment);
    Task<IReadOnlyList<GoalComment>> ListByGoalAsync(Guid workspaceId, Guid goalId, CancellationToken ct = default);
}
