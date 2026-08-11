namespace Planvexa.Api.Endpoints;

using FluentValidation;
using Planvexa.Modules.WorkManagement.Domain;

// ---- Request models ----
public sealed record CreateSpaceRequest(string Name, string? Description, string? Color, string? Icon);
public sealed record UpdateSpaceRequest(string? Name, string? Description, string? Color, string? Icon, double? Position);
public sealed record CreateFolderRequest(string Name, Guid? ParentFolderId);
public sealed record UpdateFolderRequest(string Name);
public sealed record CreateReminderRequest(DateTimeOffset RemindAtUtc, string? Note);
public sealed record CreateListRequest(Guid SpaceId, Guid? FolderId, string Name, string? Description, Guid? StatusSchemeId);
public sealed record UpdateListRequest(string? Name, string? Description);
public sealed record MoveListRequest(Guid SpaceId, Guid? FolderId);
public sealed record CreateStatusSchemeRequest(string Name, IReadOnlyList<StatusInput> Statuses);
public sealed record StatusInput(string Name, string Category, string? Color);
public sealed record SetStatusTransitionsRequest(IReadOnlyList<Guid> ToStatusIds);

public sealed record CreateTaskRequest(
    Guid ListId, string Title, string? Description, Guid? ParentId, string? Priority,
    DateTimeOffset? StartDate, DateTimeOffset? DueDate, bool? IsMilestone,
    IReadOnlyList<Guid>? AssigneeUserIds, IReadOnlyList<Guid>? TagIds, Guid? StatusId,
    Guid? TaskTypeId = null, string? CustomId = null);

public sealed record UpdateTaskRequest(
    string? Title, string? Description, string? Priority,
    DateTimeOffset? StartDate, DateTimeOffset? DueDate, bool? IsMilestone, Guid? StatusId, double? Position,
    Guid? TaskTypeId = null, string? CustomId = null);

// ---- task management completeness ----
public sealed record AddToListRequest(Guid ListId);
public sealed record CopyTaskRequest(Guid TargetListId);
public sealed record MergeTaskRequest(Guid TargetTaskId);
public sealed record TeamAssigneeRequest(Guid TeamId);
public sealed record RelationRequest(Guid RelatedTaskId);
public sealed record CreateTaskTypeRequest(string Name, string? Color, string? Icon);
public sealed record UpdateTaskTypeRequest(string Name, string? Color, string? Icon);
public sealed record EmailIngestRequest(string From, string Subject, string Body);

public sealed class CreateTaskTypeRequestValidator : AbstractValidator<CreateTaskTypeRequest>
{
    public CreateTaskTypeRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public sealed class EmailIngestRequestValidator : AbstractValidator<EmailIngestRequest>
{
    public EmailIngestRequestValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(500);
        RuleFor(x => x.From).NotEmpty().MaximumLength(320);
    }
}

public sealed record MoveTaskRequest(Guid? ListId, Guid? StatusId, double? Position);
public sealed record BulkTaskRequest(IReadOnlyList<Guid> TaskIds, Guid? StatusId, Guid? AddAssigneeUserId, DateTimeOffset? DueDate);
public sealed record AssigneeRequest(Guid UserId);
public sealed record SetTagsRequest(IReadOnlyList<Guid> TagIds);
public sealed record AddDependencyRequest(Guid DependsOnTaskId, string Type);
public sealed record CreateChecklistRequest(string Name);
public sealed record CreateChecklistItemRequest(string Content);
public sealed record UpdateChecklistItemRequest(string? Content, bool? IsResolved, double? Position);
public sealed record SetCustomFieldRequest(string? Value);
public sealed record SetCustomFieldRelationshipsRequest(IReadOnlyList<Guid> RelatedTaskIds);
public sealed record CreateTagRequest(string Name, string? Color);

// FormulaExpression is only meaningful for Type=Formula; the Rollup* fields only for
// Type=Rollup — see CreateCustomFieldRequestValidator for the per-type requirement rules.
public sealed record CreateCustomFieldRequest(
    string Name, string Type, string Scope, Guid? ScopeId, bool IsRequired, IReadOnlyList<CustomFieldOptionRequest>? Options,
    string? FormulaExpression = null,
    string? RollupSourceType = null, Guid? RollupSourceFieldId = null,
    Guid? RollupTargetFieldId = null, string? RollupFunction = null);
public sealed record CustomFieldOptionRequest(string Label, string? Color);
public sealed record CreateRecurringRequest(Guid ListId, string Title, string? Description, string? Priority, string Frequency, int Interval, string TimeZoneId, DateTimeOffset AnchorUtc);
public sealed record CreateViewRequest(string ViewType, string ScopeType, Guid? ScopeId, string Name, string? Config, bool IsPrivate);
public sealed record UpdateViewRequest(string? Name, string? Config, bool? IsPrivate);
public sealed record MoveFolderRequest(Guid? ParentFolderId);
public sealed record ReorderFolderRequest(double Position);
public sealed record SetDefaultViewRequest(Guid? ViewId);
public sealed record CreateTemplateRequest(string ResourceType, Guid SourceResourceId, string Name);
public sealed record ApplyTemplateRequest(Guid? SpaceId, Guid? FolderId, string Name);
public sealed record ToggleFavoriteRequest(string ResourceType, Guid ResourceId);
public sealed record RecordRecentItemRequest(string ResourceType, Guid ResourceId);
public sealed record SaveMyWorkPreferencesRequest(string SortBy, IReadOnlyList<string>? HiddenSections);

// ---- Validators ----
public sealed class CreateSpaceRequestValidator : AbstractValidator<CreateSpaceRequest>
{
    public CreateSpaceRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class UpdateFolderRequestValidator : AbstractValidator<UpdateFolderRequest>
{
    public UpdateFolderRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class CreateReminderRequestValidator : AbstractValidator<CreateReminderRequest>
{
    public CreateReminderRequestValidator()
    {
        RuleFor(x => x.RemindAtUtc).NotEqual(default(DateTimeOffset));
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class CreateListRequestValidator : AbstractValidator<CreateListRequest>
{
    public CreateListRequestValidator()
    {
        RuleFor(x => x.SpaceId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.ListId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Priority).Must(BeValidPriority!).When(x => x.Priority is not null)
            .WithMessage("Priority must be one of: None, Low, Normal, High, Urgent.");
    }

    private static bool BeValidPriority(string priority) => Enum.TryParse<TaskPriority>(priority, ignoreCase: true, out _);
}

public sealed class CreateRecurringRequestValidator : AbstractValidator<CreateRecurringRequest>
{
    public CreateRecurringRequestValidator()
    {
        RuleFor(x => x.ListId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Interval).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Frequency).Must(f => Enum.TryParse<RecurrenceFrequency>(f, ignoreCase: true, out _))
            .WithMessage("Frequency must be one of: Daily, Weekly, Monthly, Yearly.");
        RuleFor(x => x.TimeZoneId).NotEmpty();
    }
}

public sealed class CreateCustomFieldRequestValidator : AbstractValidator<CreateCustomFieldRequest>
{
    public CreateCustomFieldRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Type).Must(t => Enum.TryParse<CustomFieldType>(t, ignoreCase: true, out _))
            .WithMessage("Unknown custom field type.");
        RuleFor(x => x.Scope).Must(s => Enum.TryParse<CustomFieldScope>(s, ignoreCase: true, out _))
            .WithMessage("Scope must be one of: Workspace, Space, Folder, List.");

        RuleFor(x => x.FormulaExpression).NotEmpty().MaximumLength(2000)
            .When(x => string.Equals(x.Type, "Formula", StringComparison.OrdinalIgnoreCase))
            .WithMessage("A formula field requires an expression.");

        RuleFor(x => x.RollupSourceType).Must(s => Enum.TryParse<CustomFieldRollupSourceType>(s, ignoreCase: true, out _))
            .When(x => string.Equals(x.Type, "Rollup", StringComparison.OrdinalIgnoreCase))
            .WithMessage("RollupSourceType must be one of: Subtasks, RelationshipField.");

        RuleFor(x => x.RollupFunction).Must(f => Enum.TryParse<CustomFieldRollupFunction>(f, ignoreCase: true, out _))
            .When(x => string.Equals(x.Type, "Rollup", StringComparison.OrdinalIgnoreCase))
            .WithMessage("RollupFunction must be one of: Sum, Count, Average, Min, Max.");
    }
}

public sealed class CreateTemplateRequestValidator : AbstractValidator<CreateTemplateRequest>
{
    public CreateTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SourceResourceId).NotEmpty();
        RuleFor(x => x.ResourceType).Must(t => Enum.TryParse<TemplateResourceType>(t, ignoreCase: true, out _))
            .WithMessage("ResourceType must be one of: Space, Folder, List.");
    }
}

public sealed class ApplyTemplateRequestValidator : AbstractValidator<ApplyTemplateRequest>
{
    public ApplyTemplateRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class ToggleFavoriteRequestValidator : AbstractValidator<ToggleFavoriteRequest>
{
    public ToggleFavoriteRequestValidator()
    {
        RuleFor(x => x.ResourceType).NotEmpty().MaximumLength(32);
        RuleFor(x => x.ResourceId).NotEmpty();
    }
}

public sealed class RecordRecentItemRequestValidator : AbstractValidator<RecordRecentItemRequest>
{
    public RecordRecentItemRequestValidator()
    {
        RuleFor(x => x.ResourceType).NotEmpty().MaximumLength(32);
        RuleFor(x => x.ResourceId).NotEmpty();
    }
}

public sealed class SaveMyWorkPreferencesRequestValidator : AbstractValidator<SaveMyWorkPreferencesRequest>
{
    public SaveMyWorkPreferencesRequestValidator()
    {
        RuleFor(x => x.SortBy).Must(s => MyWorkPreference.ValidSortValues.Contains(s))
            .WithMessage($"SortBy must be one of: {string.Join(", ", MyWorkPreference.ValidSortValues)}.");
        RuleForEach(x => x.HiddenSections).Must(s => MyWorkPreference.ValidSections.Contains(s))
            .WithMessage($"HiddenSections entries must be one of: {string.Join(", ", MyWorkPreference.ValidSections)}.")
            .When(x => x.HiddenSections is not null);
    }
}
