namespace Planvexa.Modules.WorkManagement.Application.Services;

using System.Text.Json;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;

public sealed record CreateFromTemplateResultDto(string ResourceType, Guid Id, string Name);

// ---- Opaque structural snapshot (WorkTemplate.StructureJson) — never contains task instances/content. ----
internal sealed record TemplateCustomFieldSnapshot(string Name, string Type, bool IsRequired, IReadOnlyList<TemplateCustomFieldOptionSnapshot> Options);
internal sealed record TemplateCustomFieldOptionSnapshot(string Label, string? Color);
internal sealed record TemplateStatusSnapshot(string Name, string Category, string Color);
internal sealed record TemplateListSnapshot(string Name, string? Description, IReadOnlyList<TemplateStatusSnapshot> Statuses, IReadOnlyList<TemplateCustomFieldSnapshot> CustomFields);
internal sealed record TemplateFolderSnapshot(
    string Name, IReadOnlyList<TemplateCustomFieldSnapshot> CustomFields,
    IReadOnlyList<TemplateFolderSnapshot> Subfolders, IReadOnlyList<TemplateListSnapshot> Lists);
internal sealed record TemplateSpaceSnapshot(
    IReadOnlyList<TemplateCustomFieldSnapshot> CustomFields,
    IReadOnlyList<TemplateFolderSnapshot> Folders, IReadOnlyList<TemplateListSnapshot> Lists);

/// <summary>
/// Captures a Space/Folder/List's structure — sub-structure, status scheme and custom-field
/// definitions, never task instances/content — as a reusable <see cref="WorkTemplate"/>, and applies one
/// to create a new pre-populated resource. See TemplateSpaceSnapshot/TemplateFolderSnapshot/
/// TemplateListSnapshot for exactly what is captured.
/// </summary>
public sealed class WorkTemplateService(
    WorkServiceContext ctx,
    IWorkTemplateStore templates,
    ISpaceStore spaces,
    IFolderStore folders,
    ITaskListStore lists,
    IStatusSchemeStore schemes,
    ICustomFieldStore customFields,
    StatusSchemeService schemeService) : WorkServiceBase(ctx)
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<WorkTemplateDto> SaveAsTemplateAsync(CreateTemplateCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();

        string structureJson = command.ResourceType switch
        {
            TemplateResourceType.Space => JsonSerializer.Serialize(await BuildSpaceSnapshotAsync(command.SourceResourceId, ct), JsonOptions),
            TemplateResourceType.Folder => JsonSerializer.Serialize(await BuildFolderSnapshotAsync(command.SourceResourceId, ct), JsonOptions),
            TemplateResourceType.List => JsonSerializer.Serialize(await BuildListSnapshotAsync(command.SourceResourceId, ct), JsonOptions),
            _ => throw new ValidationAppException("Unknown template resource type."),
        };

        var template = WorkTemplate.Create(NewId(), workspaceId, command.ResourceType, command.Name, structureJson, UserId, Now);
        templates.Add(template);
        Audit("template.created", nameof(WorkTemplate), template.Id, new { command.ResourceType, sourceResourceId = command.SourceResourceId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(template);
    }

    public async Task<IReadOnlyList<WorkTemplateDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);
        var list = await templates.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(WorkMapper.ToDto).ToList();
    }

    public async Task<CreateFromTemplateResultDto> CreateFromTemplateAsync(CreateFromTemplateCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var template = await templates.FindAsync(command.TemplateId, ct);
        if (template is null || template.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Template not found.");
        }

        return template.ResourceType switch
        {
            TemplateResourceType.Space => await ApplySpaceAsync(template, command, ct),
            TemplateResourceType.Folder => await ApplyFolderAsync(template, command, ct),
            TemplateResourceType.List => await ApplyListAsync(template, command, ct),
            _ => throw new ValidationAppException("Unknown template resource type."),
        };
    }

    // ---- Build (save-as-template) ----

    private async Task<TemplateSpaceSnapshot> BuildSpaceSnapshotAsync(Guid spaceId, CancellationToken ct)
    {
        var space = await spaces.FindAsync(spaceId, ct);
        if (space is null || space.IsDeleted)
        {
            throw new NotFoundException("Space not found.");
        }

        await EnsureManageStructureAsync(space, WorkResourceTypes.Space, ct);

        var allFolders = await folders.ListBySpaceAsync(spaceId, ct);
        var allLists = await lists.ListBySpaceAsync(spaceId, ct);
        var fields = await FieldsForScopeAsync(space.WorkspaceId, CustomFieldScope.Space, spaceId, ct);

        var topFolders = new List<TemplateFolderSnapshot>();
        foreach (var folder in allFolders.Where(f => !f.IsDeleted && f.ParentFolderId is null).OrderBy(f => f.Position))
        {
            topFolders.Add(await BuildFolderSnapshotRecursiveAsync(folder, allFolders, allLists, ct));
        }

        var ungroupedLists = new List<TemplateListSnapshot>();
        foreach (var list in allLists.Where(l => !l.IsDeleted && l.FolderId is null).OrderBy(l => l.Position))
        {
            ungroupedLists.Add(await BuildListSnapshotFromEntityAsync(list, ct));
        }

        return new TemplateSpaceSnapshot(fields, topFolders, ungroupedLists);
    }

    private async Task<TemplateFolderSnapshot> BuildFolderSnapshotAsync(Guid folderId, CancellationToken ct)
    {
        var folder = await folders.FindAsync(folderId, ct);
        if (folder is null || folder.IsDeleted)
        {
            throw new NotFoundException("Folder not found.");
        }

        await EnsureManageStructureAsync(folder, WorkResourceTypes.Folder, ct);

        var allFolders = await folders.ListBySpaceAsync(folder.SpaceId, ct);
        var allLists = await lists.ListBySpaceAsync(folder.SpaceId, ct);
        return await BuildFolderSnapshotRecursiveAsync(folder, allFolders, allLists, ct);
    }

    private async Task<TemplateFolderSnapshot> BuildFolderSnapshotRecursiveAsync(
        Folder folder, IReadOnlyList<Folder> allFolders, IReadOnlyList<TaskList> allLists, CancellationToken ct)
    {
        var fields = await FieldsForScopeAsync(folder.WorkspaceId, CustomFieldScope.Folder, folder.Id, ct);

        var childLists = new List<TemplateListSnapshot>();
        foreach (var list in allLists.Where(l => !l.IsDeleted && l.FolderId == folder.Id).OrderBy(l => l.Position))
        {
            childLists.Add(await BuildListSnapshotFromEntityAsync(list, ct));
        }

        var subfolders = new List<TemplateFolderSnapshot>();
        foreach (var sub in allFolders.Where(f => !f.IsDeleted && f.ParentFolderId == folder.Id).OrderBy(f => f.Position))
        {
            subfolders.Add(await BuildFolderSnapshotRecursiveAsync(sub, allFolders, allLists, ct));
        }

        return new TemplateFolderSnapshot(folder.Name, fields, subfolders, childLists);
    }

    private async Task<TemplateListSnapshot> BuildListSnapshotAsync(Guid listId, CancellationToken ct)
    {
        var list = await lists.FindAsync(listId, ct);
        if (list is null || list.IsDeleted)
        {
            throw new NotFoundException("List not found.");
        }

        await EnsureManageStructureAsync(list, WorkResourceTypes.List, ct);
        return await BuildListSnapshotFromEntityAsync(list, ct);
    }

    private async Task<TemplateListSnapshot> BuildListSnapshotFromEntityAsync(TaskList list, CancellationToken ct)
    {
        var scheme = await schemes.FindAsync(list.StatusSchemeId, ct);
        var statuses = scheme?.Statuses.OrderBy(s => s.Position)
            .Select(s => new TemplateStatusSnapshot(s.Name, s.Category.ToString(), s.Color)).ToList()
            ?? [];
        var fields = await FieldsForScopeAsync(list.WorkspaceId, CustomFieldScope.List, list.Id, ct);
        return new TemplateListSnapshot(list.Name, list.Description, statuses, fields);
    }

    private async Task<IReadOnlyList<TemplateCustomFieldSnapshot>> FieldsForScopeAsync(
        Guid workspaceId, CustomFieldScope scope, Guid scopeId, CancellationToken ct)
    {
        var all = await customFields.ListByWorkspaceAsync(workspaceId, ct);
        return all.Where(d => d.Scope == scope && d.ScopeId == scopeId)
            .OrderBy(d => d.Position)
            .Select(d => new TemplateCustomFieldSnapshot(
                d.Name, d.Type.ToString(), d.IsRequired,
                d.Options.OrderBy(o => o.Position).Select(o => new TemplateCustomFieldOptionSnapshot(o.Label, o.Color)).ToList()))
            .ToList();
    }

    // ---- Apply (create-from-template) ----

    private async Task<CreateFromTemplateResultDto> ApplySpaceAsync(WorkTemplate template, CreateFromTemplateCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);

        var snapshot = JsonSerializer.Deserialize<TemplateSpaceSnapshot>(template.StructureJson, JsonOptions)
            ?? throw new ValidationAppException("Template is corrupt.");

        var max = await spaces.MaxPositionAsync(workspaceId, ct);
        var space = Space.Create(NewId(), workspaceId, command.Name, Positioning.Append(max), UserId, Now);
        spaces.Add(space);

        CreateFields(workspaceId, CustomFieldScope.Space, space.Id, snapshot.CustomFields);
        foreach (var folder in snapshot.Folders)
        {
            await ApplyFolderRecursiveAsync(workspaceId, space.Id, null, folder, ct);
        }

        foreach (var list in snapshot.Lists)
        {
            await ApplyListFromSnapshotAsync(workspaceId, space.Id, null, list, ct);
        }

        Audit("space.created_from_template", nameof(Space), space.Id, new { templateId = template.Id });
        await SaveAsync(ct);
        return new CreateFromTemplateResultDto(nameof(Space), space.Id, space.Name);
    }

    private async Task<CreateFromTemplateResultDto> ApplyFolderAsync(WorkTemplate template, CreateFromTemplateCommand command, CancellationToken ct)
    {
        if (command.SpaceId is not { } targetSpaceId)
        {
            throw new ValidationAppException("A target spaceId is required to create a folder from a template.");
        }

        var space = await spaces.FindAsync(targetSpaceId, ct);
        if (space is null || space.IsDeleted)
        {
            throw new NotFoundException("Space not found.");
        }

        await EnsureManageStructureAsync(space, WorkResourceTypes.Space, ct);

        if (command.FolderId is { } parentFolderId)
        {
            var parent = await folders.FindAsync(parentFolderId, ct);
            if (parent is null || parent.IsDeleted || parent.SpaceId != space.Id)
            {
                throw new NotFoundException("Parent folder not found in this space.");
            }
        }

        var snapshot = JsonSerializer.Deserialize<TemplateFolderSnapshot>(template.StructureJson, JsonOptions)
            ?? throw new ValidationAppException("Template is corrupt.");
        var named = snapshot with { Name = command.Name };
        var folder = await ApplyFolderRecursiveAsync(space.WorkspaceId, space.Id, command.FolderId, named, ct);

        Audit("folder.created_from_template", nameof(Folder), folder.Id, new { templateId = template.Id });
        await SaveAsync(ct);
        return new CreateFromTemplateResultDto(nameof(Folder), folder.Id, folder.Name);
    }

    private async Task<CreateFromTemplateResultDto> ApplyListAsync(WorkTemplate template, CreateFromTemplateCommand command, CancellationToken ct)
    {
        if (command.SpaceId is not { } targetSpaceId)
        {
            throw new ValidationAppException("A target spaceId is required to create a list from a template.");
        }

        var space = await spaces.FindAsync(targetSpaceId, ct);
        if (space is null || space.IsDeleted)
        {
            throw new NotFoundException("Space not found.");
        }

        await EnsureManageStructureAsync(space, WorkResourceTypes.Space, ct);

        if (command.FolderId is { } folderId)
        {
            var folder = await folders.FindAsync(folderId, ct);
            if (folder is null || folder.IsDeleted || folder.SpaceId != space.Id)
            {
                throw new NotFoundException("Folder not found in this space.");
            }
        }

        var snapshot = JsonSerializer.Deserialize<TemplateListSnapshot>(template.StructureJson, JsonOptions)
            ?? throw new ValidationAppException("Template is corrupt.");
        var named = snapshot with { Name = command.Name };
        var list = await ApplyListFromSnapshotAsync(space.WorkspaceId, space.Id, command.FolderId, named, ct);

        Audit("list.created_from_template", nameof(TaskList), list.Id, new { templateId = template.Id });
        await SaveAsync(ct);
        return new CreateFromTemplateResultDto(nameof(TaskList), list.Id, list.Name);
    }

    private async Task<Folder> ApplyFolderRecursiveAsync(
        Guid workspaceId, Guid spaceId, Guid? parentFolderId, TemplateFolderSnapshot snapshot, CancellationToken ct)
    {
        var max = await folders.MaxPositionAsync(spaceId, ct);
        var folder = Folder.Create(NewId(), workspaceId, spaceId, parentFolderId, snapshot.Name, Positioning.Append(max), UserId, Now);
        folders.Add(folder);

        CreateFields(workspaceId, CustomFieldScope.Folder, folder.Id, snapshot.CustomFields);

        foreach (var list in snapshot.Lists)
        {
            await ApplyListFromSnapshotAsync(workspaceId, spaceId, folder.Id, list, ct);
        }

        foreach (var sub in snapshot.Subfolders)
        {
            await ApplyFolderRecursiveAsync(workspaceId, spaceId, folder.Id, sub, ct);
        }

        return folder;
    }

    private async Task<TaskList> ApplyListFromSnapshotAsync(
        Guid workspaceId, Guid spaceId, Guid? folderId, TemplateListSnapshot snapshot, CancellationToken ct)
    {
        var schemeStatuses = snapshot.Statuses
            .Select(s => (s.Name, Enum.Parse<StatusCategory>(s.Category, ignoreCase: true), (string?)s.Color))
            .ToList();
        var scheme = schemeStatuses.Count > 0
            ? await schemeService.CreateAsync($"{snapshot.Name} statuses", schemeStatuses, ct)
            : null;

        Guid schemeId;
        if (scheme is not null)
        {
            schemeId = scheme.Id;
        }
        else
        {
            var defaultScheme = await schemes.FindDefaultAsync(workspaceId, ct)
                ?? throw new ValidationAppException("No default status scheme available for the target workspace.");
            schemeId = defaultScheme.Id;
        }

        var max = await lists.MaxPositionAsync(spaceId, ct);
        var list = TaskList.Create(NewId(), workspaceId, spaceId, folderId, snapshot.Name, schemeId, Positioning.Append(max), UserId, Now);
        list.Update(null, snapshot.Description, UserId, Now);
        lists.Add(list);

        CreateFields(workspaceId, CustomFieldScope.List, list.Id, snapshot.CustomFields);
        return list;
    }

    private void CreateFields(
        Guid workspaceId, CustomFieldScope scope, Guid scopeId, IReadOnlyList<TemplateCustomFieldSnapshot> fields)
    {
        double position = Positioning.Step;
        foreach (var field in fields)
        {
            var type = Enum.Parse<CustomFieldType>(field.Type, ignoreCase: true);
            var definition = CustomFieldDefinition.Create(NewId(), workspaceId, scope, scopeId, field.Name, type, field.IsRequired, position);
            double optionPos = Positioning.Step;
            foreach (var option in field.Options)
            {
                definition.AddOption(NewId(), option.Label, option.Color, optionPos);
                optionPos += Positioning.Step;
            }

            customFields.Add(definition);
            position += Positioning.Step;
        }
    }
}
