namespace Planvexa.Modules.WorkManagement.Domain;

/// <summary>Task priority. Ordered so numeric comparison expresses urgency.</summary>
public enum TaskPriority
{
    None = 0,
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4,
}

/// <summary>
/// The lifecycle category a status belongs to. Completion is defined by the <see cref="Done"/> and
/// <see cref="Closed"/> categories, independent of the status' display name.
/// </summary>
public enum StatusCategory
{
    NotStarted = 0,
    Active = 1,
    Done = 2,
    Closed = 3,
}

/// <summary>Direction/kind of a dependency between two tasks.</summary>
public enum DependencyType
{
    /// <summary>This task is blocked by the other task (the other must finish first).</summary>
    BlockedBy = 0,

    /// <summary>This task waits on the other (soft dependency, does not block completion).</summary>
    WaitingOn = 1,

    /// <summary>This task blocks the other task.</summary>
    Blocks = 2,
}

public enum CustomFieldType
{
    Text = 0,
    LongText = 1,
    Number = 2,
    Currency = 3,
    Boolean = 4,
    Date = 5,
    DateTime = 6,
    Dropdown = 7,
    MultiSelect = 8,
    Url = 9,
    Email = 10,
    Rating = 11,

    /// <summary>References a workspace member; stores their user id (CustomFieldValue.UserValue).</summary>
    User = 12,

    /// <summary>References a Tenancy Team by opaque id (CustomFieldValue.TeamValue) — see
    /// TaskTeamAssignee's doc comment for why WorkManagement never resolves/validates the Team itself.</summary>
    Team = 13,

    /// <summary>Free text with basic format validation, stored in TextValue.</summary>
    Phone = 14,

    /// <summary>A free-text address string (the simpler of the two options considered,
    /// over structured lat/lng), stored in TextValue.</summary>
    Location = 15,

    /// <summary>A 0-100 numeric percentage (not a 0-1 fraction), stored in NumberValue.</summary>
    Progress = 16,

    /// <summary>Computed from other fields on the same task via
    /// CustomFieldDefinition.FormulaExpression — never has a stored CustomFieldValue row.</summary>
    Formula = 17,

    /// <summary>Links this task to one or more other tasks, workspace-scoped — see
    /// CustomFieldRelationshipValue (a dedicated join table keyed by field definition, unlike the fixed
    /// TaskRelation).</summary>
    Relationship = 18,

    /// <summary>Aggregates a target field across subtasks or a Relationship field's linked
    /// tasks — never has a stored CustomFieldValue row, computed at read time like Formula.</summary>
    Rollup = 19,
}

/// <summary>Where a Rollup field's source tasks come from.</summary>
public enum CustomFieldRollupSourceType
{
    /// <summary>Direct subtasks (WorkItem.ParentId == this task) — immediate children only, not the full tree.</summary>
    Subtasks = 0,

    /// <summary>Tasks linked via a specific Relationship-type custom field on this same definition set (see
    /// CustomFieldDefinition.RollupSourceFieldId).</summary>
    RelationshipField = 1,
}

/// <summary>The aggregation function a Rollup field applies across its source tasks.</summary>
public enum CustomFieldRollupFunction
{
    Sum = 0,
    Count = 1,
    Average = 2,
    Min = 3,
    Max = 4,
}

/// <summary>Where a custom-field definition applies.</summary>
public enum CustomFieldScope
{
    Workspace = 0,
    Space = 1,
    List = 2,

    /// <summary>Scoped to a Folder; inherited by every List nested under it (see CustomFieldResolution).</summary>
    Folder = 3,
}

public enum SavedViewType
{
    List = 0,
    Table = 1,
    Board = 2,
    Calendar = 3,
    Timeline = 4,
    Gantt = 5,
}

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3,
}
