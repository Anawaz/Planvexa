namespace Planvexa.SharedContracts.Work;

/// <summary>
/// Write-side contract (implemented by WorkManagement) that lets other modules create and mutate tasks
/// without depending on WorkManagement internals (AGENTS.md rule 7). Serves Forms (create a task from a
/// submission) and Automations (apply actions). Runs under the ambient tenant. These operations act as
/// a system/automation actor — the CALLER is responsible for any user-facing authorization; the target
/// task/list is always re-validated to belong to the ambient tenant.
/// </summary>
public interface ITaskWriteApi
{
    /// <summary>Creates a task in the given list. Returns the new task id, or null if the list is missing.</summary>
    Task<Guid?> CreateTaskAsync(Guid listId, string title, string? description, CancellationToken cancellationToken = default);

    /// <summary>Sets the task's status by (case-insensitive) status name within its list scheme. No-op if already set; false if task/status missing.</summary>
    Task<bool> SetStatusByNameAsync(Guid taskId, string statusName, CancellationToken cancellationToken = default);

    /// <summary>Assigns a user to the task. Idempotent. False if the task is missing.</summary>
    Task<bool> AssignAsync(Guid taskId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a tag by name to the task, creating the tag if needed. Idempotent. False if the task is missing.</summary>
    Task<bool> AddTagByNameAsync(Guid taskId, string tagName, CancellationToken cancellationToken = default);

    /// <summary>Forms full routing: sets the task's priority by (case-insensitive) enum name
    /// ("Low"/"Normal"/"High"/"Urgent"/"None"). False if the task is missing or the name is unknown.</summary>
    Task<bool> SetPriorityByNameAsync(Guid taskId, string priorityName, CancellationToken cancellationToken = default);

    /// <summary>Forms full routing: sets the task's due date. False if the task is missing.</summary>
    Task<bool> SetDueDateAsync(Guid taskId, DateTimeOffset dueDate, CancellationToken cancellationToken = default);

    /// <summary>Forms full routing: assigns a Team (opaque cross-module id, unvalidated — same
    /// pattern as <c>CustomFieldValue.TeamValue</c>) to the task. Idempotent. False if the task is missing.</summary>
    Task<bool> AssignTeamAsync(Guid taskId, Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>Forms custom-field mapping: sets a task's value for a WorkManagement custom
    /// field definition, coercing the raw string per the field's type. Best-effort: returns false (does
    /// NOT throw) if the task/definition is missing, belongs to a different workspace, or the field is
    /// computed (Formula/Rollup), a Relationship field, a User field, or the raw value fails validation —
    /// callers that don't want a submission's task creation to fail over one bad field mapping treat false
    /// as "skipped".</summary>
    Task<bool> SetCustomFieldValueAsync(Guid taskId, Guid definitionId, string? rawValue, CancellationToken cancellationToken = default);

    /// <summary>Forms file-upload fields: attaches an already-stored file (same shared
    /// <c>IFileStorage</c> backend, no byte copy) to the task as a WorkManagement task attachment. False
    /// if the task is missing.</summary>
    Task<bool> AttachFileAsync(Guid taskId, string storagePath, string fileName, string contentType, long sizeBytes, CancellationToken cancellationToken = default);
}
