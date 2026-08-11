namespace Planvexa.Modules.Planning.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A planning-owned estimate of effort for a task, in whole seconds. Kept in the Planning module so
/// WorkManagement's schema stays stable and estimates can evolve (points, ranges) independently.
/// </summary>
public sealed class TaskEstimate : Entity, IWorkspaceOwned
{
    private TaskEstimate()
    {
    }

    private TaskEstimate(Guid id, Guid workspaceId, Guid taskId, long estimateSeconds, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        TaskId = taskId;
        EstimateSeconds = estimateSeconds;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid TaskId { get; private set; }
    public long EstimateSeconds { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TaskEstimate Create(Guid id, Guid workspaceId, Guid taskId, long estimateSeconds, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(taskId, nameof(taskId));
        if (estimateSeconds < 0)
        {
            throw new ValidationAppException("Estimate cannot be negative.");
        }

        return new TaskEstimate(id, workspaceId, taskId, estimateSeconds, nowUtc);
    }

    public void Set(long estimateSeconds, DateTimeOffset nowUtc)
    {
        if (estimateSeconds < 0)
        {
            throw new ValidationAppException("Estimate cannot be negative.");
        }

        EstimateSeconds = estimateSeconds;
        UpdatedAtUtc = nowUtc;
    }
}
