namespace Planvexa.Modules.WorkManagement.Application;

using Planvexa.Modules.WorkManagement.Domain;

// ---- Commands ----
public sealed record CreateSpaceCommand(string Name, string? Description, string? Color, string? Icon);
public sealed record UpdateSpaceCommand(string? Name, string? Description, string? Color, string? Icon, double? Position);
public sealed record CreateFolderCommand(Guid SpaceId, Guid? ParentFolderId, string Name);
public sealed record CreateListCommand(Guid SpaceId, Guid? FolderId, string Name, string? Description, Guid? StatusSchemeId);
public sealed record UpdateListCommand(string? Name, string? Description);

public sealed record CreateReminderCommand(Guid TaskId, DateTimeOffset RemindAtUtc, string? Note);
public sealed record ReminderDto(Guid Id, Guid TaskId, DateTimeOffset RemindAtUtc, string? Note, bool IsSent);

public sealed record CreateTaskCommand(
    Guid ListId, string Title, string? Description, Guid? ParentId, TaskPriority? Priority,
    DateTimeOffset? StartDate, DateTimeOffset? DueDate, bool? IsMilestone,
    IReadOnlyList<Guid>? AssigneeUserIds, IReadOnlyList<Guid>? TagIds, Guid? StatusId,
    Guid? TaskTypeId = null, string? CustomId = null);

// Note: TaskTypeId/CustomId follow the same convention as every other UpdateTaskCommand
// field — null means "leave unchanged" (see WorkItem.UpdateDetails); there is no way to clear one back to
// null through this endpoint, consistent with Title/Description/etc. today.
public sealed record UpdateTaskCommand(
    string? Title, string? Description, TaskPriority? Priority,
    DateTimeOffset? StartDate, DateTimeOffset? DueDate, bool? IsMilestone, Guid? StatusId, double? Position,
    Guid? TaskTypeId = null, string? CustomId = null);

public sealed record MoveTaskCommand(Guid? ListId, Guid? StatusId, double? Position);
public sealed record BulkTaskUpdate(IReadOnlyList<Guid> TaskIds, Guid? StatusId, Guid? AddAssigneeUserId, DateTimeOffset? DueDate);

// ---- Importers ----
public sealed record ImportJobDto(
    Guid Id, string SourceType, string FileName, string Status, IReadOnlyList<string> DetectedColumns,
    string? ColumnMappingJson, string? TargetSpaceName, string? TargetListName, int TotalRows, int CommittedRows,
    int ErrorCount, DateTimeOffset CreatedAtUtc);

public sealed record ImportJobRowDto(Guid Id, int RowIndex, string Status, string? ErrorMessage, Guid? CreatedTaskId);
public sealed record AddDependencyCommand(Guid DependsOnTaskId, DependencyType Type);
public sealed record CreateCustomFieldCommand(
    string Name, CustomFieldType Type, CustomFieldScope Scope, Guid? ScopeId, bool IsRequired, IReadOnlyList<CustomFieldOptionInput>? Options,
    string? FormulaExpression = null,
    CustomFieldRollupSourceType? RollupSourceType = null, Guid? RollupSourceFieldId = null,
    Guid? RollupTargetFieldId = null, CustomFieldRollupFunction? RollupFunction = null);
public sealed record CustomFieldOptionInput(string Label, string? Color);

/// <summary>Full replacement of a task's linked tasks for one Relationship-type field
/// (same "replace the whole set" convention as WorkItemService.SetTagsAsync).</summary>
public sealed record SetRelationshipValuesCommand(IReadOnlyList<Guid> RelatedTaskIds);
public sealed record CreateRecurringCommand(Guid ListId, string Title, string? Description, TaskPriority? Priority, RecurrenceFrequency Frequency, int Interval, string TimeZoneId, DateTimeOffset AnchorUtc);
public sealed record CreateViewCommand(SavedViewType ViewType, CustomFieldScope ScopeType, Guid? ScopeId, string Name, string ConfigJson, bool IsPrivate);

public sealed record MoveFolderCommand(Guid? NewParentFolderId);
public sealed record SetDefaultViewCommand(Guid? ViewId);
public sealed record CreateTemplateCommand(TemplateResourceType ResourceType, Guid SourceResourceId, string Name);
public sealed record CreateFromTemplateCommand(Guid TemplateId, Guid? SpaceId, Guid? FolderId, string Name);
public sealed record ToggleFavoriteCommand(string ResourceType, Guid ResourceId);
public sealed record RecordRecentItemCommand(string ResourceType, Guid ResourceId);
public sealed record RecentItemDto(string ResourceType, Guid ResourceId, DateTimeOffset ViewedAtUtc);

// ---- My Work personal sort/organize preferences (product spec section 15) ----
public sealed record SaveMyWorkPreferenceCommand(string SortBy, IReadOnlyList<string> HiddenSections);
public sealed record MyWorkPreferenceDto(string SortBy, IReadOnlyList<string> HiddenSections);

// ---- task management completeness ----
public sealed record CreateTaskTypeCommand(string Name, string? Color, string? Icon);
public sealed record UpdateTaskTypeCommand(string Name, string? Color, string? Icon);
public sealed record TaskTypeDto(Guid Id, string Name, string Color, string? Icon, bool IsBuiltIn, double Position);

public sealed record TaskListMembershipDto(Guid ListId, bool IsPrimary, double Position, DateTimeOffset AddedAtUtc);
public sealed record TaskRelationDto(Guid RelatedTaskId, DateTimeOffset CreatedAtUtc);

// ---- Read models ----
public sealed record SpaceDto(Guid Id, string Name, string? Description, string? Color, string? Icon, double Position, bool IsArchived, bool IsPrivate, Guid? DefaultViewId);
public sealed record FolderDto(Guid Id, Guid SpaceId, Guid? ParentFolderId, string Name, double Position, bool IsPrivate, Guid? DefaultViewId, bool IsArchived);
public sealed record ListDto(Guid Id, Guid SpaceId, Guid? FolderId, string Name, string? Description, Guid StatusSchemeId, double Position, bool IsArchived, bool IsPrivate, Guid? DefaultViewId);
public sealed record WorkTemplateDto(Guid Id, string ResourceType, string Name, DateTimeOffset CreatedAtUtc);
public sealed record WorkFavoriteDto(Guid Id, string ResourceType, Guid ResourceId, DateTimeOffset CreatedAtUtc);
public sealed record StatusDto(Guid Id, string Name, string Category, string Color, double Position, IReadOnlyList<Guid> AllowedNextStatusIds);
/// <summary><paramref name="SpaceId"/> null = a workspace-level scheme; set = that Space's override.</summary>
public sealed record StatusSchemeDto(Guid Id, string Name, bool IsDefault, IReadOnlyList<StatusDto> Statuses, Guid? SpaceId);

/// <summary>A Space's effective scheme plus whether it is the Space's own override or the inherited workspace default.</summary>
public sealed record SpaceStatusSchemeDto(StatusSchemeDto Scheme, bool IsCustomized);

/// <summary>Explicit "move the tasks sitting on FromStatusId onto ToStatusId" instruction (see B2).</summary>
public sealed record StatusMappingInput(Guid FromStatusId, Guid ToStatusId);
public sealed record TagDto(Guid Id, string Name, string Color);

public sealed record TaskDto(
    Guid Id, Guid ListId, Guid SpaceId, Guid? ParentId, long Sequence, string Title, string? Description,
    Guid StatusId, string Priority, DateTimeOffset? StartDate, DateTimeOffset? DueDate, bool IsMilestone,
    bool IsCompleted, double Position, IReadOnlyList<Guid> AssigneeUserIds, IReadOnlyList<Guid> TagIds, bool IsPrivate,
    Guid? TaskTypeId, string? CustomId, IReadOnlyList<Guid> TeamAssigneeIds, bool IsArchived, Guid? CreatedByUserId);

public sealed record ChecklistItemDto(Guid Id, string Content, bool IsResolved, double Position);
public sealed record ChecklistDto(Guid Id, string Name, double Position, IReadOnlyList<ChecklistItemDto> Items);
public sealed record DependencyDto(Guid Id, Guid DependsOnTaskId, string Type);
// ComputedError is set instead of a value when a Formula/Rollup field failed to evaluate for
// this task (e.g. an unresolved dependency, division by zero, or no source data for the aggregation) —
// surfaced as an error rather than silently defaulting to zero/null.
public sealed record CustomFieldValueDto(
    Guid DefinitionId, string? Text, decimal? Number, DateTimeOffset? Date, bool? Boolean, Guid? OptionId,
    Guid? UserValue = null, Guid? TeamValue = null, IReadOnlyList<Guid>? RelatedTaskIds = null, string? ComputedError = null);
public sealed record ActivityDto(Guid Id, Guid? ActorUserId, string Type, string? Data, DateTimeOffset CreatedAtUtc);

/// <summary>Map view: one task's Location-field value, grouped/sorted by the frontend.</summary>
public sealed record LocationValueDto(Guid TaskId, string TaskTitle, string Location);
public sealed record AttachmentDto(
    Guid Id, Guid TaskId, string FileName, string ContentType, long SizeBytes,
    Guid UploadedByUserId, DateTimeOffset CreatedAtUtc);

public sealed record TaskDetailDto(
    TaskDto Task,
    IReadOnlyList<Guid> WatcherUserIds,
    IReadOnlyList<ChecklistDto> Checklists,
    IReadOnlyList<DependencyDto> Dependencies,
    IReadOnlyList<CustomFieldValueDto> CustomFieldValues,
    IReadOnlyList<ActivityDto> Activity,
    IReadOnlyList<TaskListMembershipDto> Lists,
    IReadOnlyList<TaskRelationDto> Relations);

public sealed record CustomFieldDefinitionDto(
    Guid Id, string Name, string Type, string Scope, Guid? ScopeId, bool IsRequired, double Position, IReadOnlyList<CustomFieldOptionDto> Options,
    string? FormulaExpression = null,
    string? RollupSourceType = null, Guid? RollupSourceFieldId = null,
    Guid? RollupTargetFieldId = null, string? RollupFunction = null);
public sealed record CustomFieldOptionDto(Guid Id, string Label, string? Color, double Position);
public sealed record RecurringDto(Guid Id, Guid ListId, string Title, string Frequency, int Interval, string TimeZoneId, DateTimeOffset NextRunUtc, bool IsActive);
public sealed record ViewDto(Guid Id, string Name, string ViewType, string ScopeType, Guid? ScopeId, string ConfigJson, bool IsPrivate);
public sealed record GeneratedOccurrenceDto(Guid DefinitionId, bool Generated, Guid? TaskId);
