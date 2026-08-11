namespace Planvexa.Modules.WorkManagement.Application;

using Planvexa.Modules.WorkManagement.Domain;

internal static class WorkMapper
{
    public static SpaceDto ToDto(Space s) => new(s.Id, s.Name, s.Description, s.Color, s.Icon, s.Position, s.IsArchived, s.IsPrivate, s.DefaultViewId);

    public static FolderDto ToDto(Folder f) => new(f.Id, f.SpaceId, f.ParentFolderId, f.Name, f.Position, f.IsPrivate, f.DefaultViewId);

    public static ListDto ToDto(TaskList l) => new(l.Id, l.SpaceId, l.FolderId, l.Name, l.Description, l.StatusSchemeId, l.Position, l.IsArchived, l.IsPrivate, l.DefaultViewId);

    public static WorkTemplateDto ToDto(WorkTemplate t) => new(t.Id, t.ResourceType.ToString(), t.Name, t.CreatedAtUtc);

    public static WorkFavoriteDto ToDto(WorkFavorite f) => new(f.Id, f.ResourceType, f.ResourceId, f.CreatedAtUtc);

    public static StatusDto ToDto(StatusDefinition s) => new(s.Id, s.Name, s.Category.ToString(), s.Color, s.Position);

    public static StatusSchemeDto ToDto(StatusScheme s)
        => new(s.Id, s.Name, s.IsDefault, s.Statuses.OrderBy(x => x.Position).Select(ToDto).ToList());

    public static TagDto ToDto(Tag t) => new(t.Id, t.Name, t.Color);

    public static TaskDto ToDto(WorkItem t) => new(
        t.Id, t.ListId, t.SpaceId, t.ParentId, t.Sequence, t.Title, t.Description,
        t.StatusId, t.Priority.ToString(), t.StartDate, t.DueDate, t.IsMilestone, t.IsCompleted, t.Position,
        t.Assignees.Select(a => a.UserId).ToList(), t.Tags.Select(x => x.TagId).ToList(), t.IsPrivate,
        t.TaskTypeId, t.CustomId, t.TeamAssignees.Select(a => a.TeamId).ToList());

    public static TaskTypeDto ToDto(TaskType t) => new(t.Id, t.Name, t.Color, t.Icon, t.IsBuiltIn, t.Position);

    public static TaskListMembershipDto ToDto(TaskListMembership m) => new(m.ListId, m.IsPrimary, m.Position, m.AddedAtUtc);

    public static TaskRelationDto ToRelationDto(TaskRelation r, Guid fromTaskId)
        => new(r.TaskId == fromTaskId ? r.RelatedTaskId : r.TaskId, r.CreatedAtUtc);

    public static CustomFieldValueDto ToDto(CustomFieldValue v)
        => new(v.DefinitionId, v.TextValue, v.NumberValue, v.DateValue, v.BoolValue, v.OptionId, v.UserValue, v.TeamValue);

    public static ActivityDto ToDto(TaskActivityEvent a) => new(a.Id, a.ActorUserId, a.Type, a.Data, a.CreatedAtUtc);

    public static AttachmentDto ToDto(TaskAttachment a)
        => new(a.Id, a.TaskId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedByUserId, a.CreatedAtUtc);

    public static DependencyDto ToDto(TaskDependency d) => new(d.Id, d.DependsOnTaskId, d.Type.ToString());

    public static ChecklistDto ToDto(TaskChecklist c) => new(
        c.Id, c.Name, c.Position,
        c.Items.OrderBy(i => i.Position).Select(i => new ChecklistItemDto(i.Id, i.Content, i.IsResolved, i.Position)).ToList());

    public static CustomFieldDefinitionDto ToDto(CustomFieldDefinition d) => new(
        d.Id, d.Name, d.Type.ToString(), d.Scope.ToString(), d.ScopeId, d.IsRequired, d.Position,
        d.Options.OrderBy(o => o.Position).Select(o => new CustomFieldOptionDto(o.Id, o.Label, o.Color, o.Position)).ToList(),
        d.FormulaExpression, d.RollupSourceType?.ToString(), d.RollupSourceFieldId, d.RollupTargetFieldId, d.RollupFunction?.ToString());

    public static RecurringDto ToDto(RecurringTaskDefinition r)
        => new(r.Id, r.ListId, r.Title, r.Frequency.ToString(), r.Interval, r.TimeZoneId, r.NextRunUtc, r.IsActive);

    public static ViewDto ToDto(SavedView v)
        => new(v.Id, v.Name, v.ViewType.ToString(), v.ScopeType.ToString(), v.ScopeId, v.ConfigJson, v.IsPrivate);
}
