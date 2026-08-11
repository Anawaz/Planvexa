namespace Planvexa.Modules.Planning.Application;

using Planvexa.Modules.Planning.Domain;

public interface IWorkScheduleStore
{
    void Add(WorkSchedule schedule);
    Task<WorkSchedule?> FindAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IHolidayStore
{
    void Add(Holiday holiday);
    void Remove(Holiday holiday);
    Task<Holiday?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Holiday>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<Holiday>> ListInRangeAsync(Guid workspaceId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
}

public interface ILeaveStore
{
    void Add(LeaveEntry entry);
    void Remove(LeaveEntry entry);
    Task<LeaveEntry?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveEntry>> ListByWorkspaceAsync(Guid workspaceId, Guid? userId, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveEntry>> ListForUserInRangeAsync(Guid workspaceId, Guid userId, DateTime fromDate, DateTime toDate, CancellationToken ct = default);
}

public interface IEstimateStore
{
    void Add(TaskEstimate estimate);
    Task<TaskEstimate?> FindAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskEstimate>> ListByTaskIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default);
}

public interface ISprintStore
{
    void Add(Sprint sprint);
    void Remove(Sprint sprint);
    Task<Sprint?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Sprint?> FindWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Sprint>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}
