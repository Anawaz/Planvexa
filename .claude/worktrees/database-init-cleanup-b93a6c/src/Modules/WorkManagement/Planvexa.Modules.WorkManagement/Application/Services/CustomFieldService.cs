namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Formulas;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;

public sealed class CustomFieldService(
    WorkServiceContext ctx, IWorkItemStore tasks, ICustomFieldStore customFields,
    ITaskListStore lists, IFolderStore folders) : WorkServiceBase(ctx)
{
    /// <summary>Value-producing simple field types a Formula expression or a Rollup's target field may
    /// reference — Text/Date/etc. carry no numeric meaning, and Formula/Rollup themselves are excluded
    /// from being a Rollup target (see CustomFieldDefinition.RollupTargetFieldId's doc comment on why).</summary>
    private static readonly HashSet<CustomFieldType> NumericTypes =
    [
        CustomFieldType.Number, CustomFieldType.Currency, CustomFieldType.Rating,
        CustomFieldType.Progress, CustomFieldType.Boolean,
    ];

    public async Task<CustomFieldDefinitionDto> CreateAsync(CreateCustomFieldCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);

        var siblings = await customFields.ListByWorkspaceAsync(workspaceId, ct);
        var newId = NewId();

        IReadOnlyList<Guid>? formulaDependencyIds = null;
        if (command.Type == CustomFieldType.Formula)
        {
            formulaDependencyIds = ResolveFormulaDependencies(command.Name, command.FormulaExpression, siblings, newId);
        }

        if (command.Type == CustomFieldType.Rollup)
        {
            ValidateRollupDefinition(command, siblings, workspaceId);
        }

        var definition = CustomFieldDefinition.Create(
            newId, workspaceId, command.Scope, command.ScopeId, command.Name, command.Type, command.IsRequired, Positioning.Step,
            command.FormulaExpression, formulaDependencyIds,
            command.RollupSourceType, command.RollupSourceFieldId, command.RollupTargetFieldId, command.RollupFunction);

        double optionPos = Positioning.Step;
        foreach (var option in command.Options ?? [])
        {
            definition.AddOption(NewId(), option.Label, option.Color, optionPos);
            optionPos += Positioning.Step;
        }

        customFields.Add(definition);
        Audit("custom_field.created", nameof(CustomFieldDefinition), definition.Id, new { command.Name, type = command.Type.ToString() });
        await SaveAsync(ct);
        return WorkMapper.ToDto(definition);
    }

    public async Task<IReadOnlyList<CustomFieldDefinitionDto>> ListAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);
        var list = await customFields.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(WorkMapper.ToDto).ToList();
    }

    /// <summary>
    /// The custom fields actually available on a List's tasks — every Workspace-scoped field,
    /// the List's Space, every ancestor Folder of the List (so a field defined on a Folder is inherited
    /// by every List nested under it, at any depth), and the List itself. See
    /// <see cref="CustomFieldResolution"/> for the pure filter this wraps.
    /// </summary>
    public async Task<IReadOnlyList<CustomFieldDefinitionDto>> ListEffectiveForListAsync(Guid listId, CancellationToken ct = default)
    {
        var list = await lists.FindAsync(listId, ct) ?? throw new NotFoundException("List not found.");
        await EnsureReadAsync(list, WorkResourceTypes.List, ct);

        var ancestorFolderIds = await ResolveAncestorFolderIdsAsync(list, ct);
        var all = await customFields.ListByWorkspaceAsync(list.WorkspaceId, ct);
        var effective = CustomFieldResolution.EffectiveForList(all, list.SpaceId, ancestorFolderIds, list.Id);
        return effective.Select(WorkMapper.ToDto).ToList();
    }

    /// <summary>
    /// Map view -- since Location fields store a free-text address (not lat/lng) for Location fields, this
    /// is a location-grouped LIST, not an actual map widget (see the design brief: real map-tile
    /// rendering needs a maps-provider credential decision that's not this agent's to make). Returns
    /// every task in <paramref name="listId"/> the caller can read (same ACL loop as ListByListAsync)
    /// that has a non-empty value for the given Location-type field.
    /// ponytail: one FindValueAsync call per candidate task (no bulk "values for list" store method
    /// exists yet) -- fine at current list sizes; add ICustomFieldStore.ListValuesForListAsync if a list
    /// with many tasks makes this slow.
    /// </summary>
    public async Task<IReadOnlyList<LocationValueDto>> ListLocationValuesAsync(Guid listId, Guid definitionId, CancellationToken ct = default)
    {
        var list = await lists.FindAsync(listId, ct) ?? throw new NotFoundException("List not found.");
        await EnsureReadAsync(list, WorkResourceTypes.List, ct);

        var definition = await customFields.FindAsync(definitionId, ct) ?? throw new NotFoundException("Custom field not found.");
        if (definition.Type != CustomFieldType.Location)
        {
            throw new ValidationAppException("The specified field is not a Location field.");
        }

        var candidates = await tasks.ListByListAsync(listId, ct);
        var result = new List<LocationValueDto>();
        foreach (var task in candidates.Where(t => !t.IsDeleted))
        {
            if (!await CanReadInListContextAsync(task, listId, ct))
            {
                continue;
            }

            var value = await customFields.FindValueAsync(task.Id, definitionId, ct);
            if (!string.IsNullOrWhiteSpace(value?.TextValue))
            {
                result.Add(new LocationValueDto(task.Id, task.Title, value!.TextValue!));
            }
        }

        return result;
    }

    /// <summary>
    /// The effective values for one task — stored values for simple types, linked task
    /// ids for Relationship fields, and read-time-computed values for Formula/Rollup fields (evaluated in
    /// dependency order — see CustomFieldDependencyGraph). Callers must have already read-authorized
    /// <paramref name="task"/> (WorkItemService.GetAsync does, before calling this).
    /// </summary>
    public async Task<IReadOnlyList<CustomFieldValueDto>> ListEffectiveValuesForTaskAsync(WorkItem task, CancellationToken ct = default)
    {
        var all = await customFields.ListByWorkspaceAsync(task.WorkspaceId, ct);
        IReadOnlyList<CustomFieldDefinition> effectiveDefs;

        var list = await lists.FindAsync(task.ListId, ct);
        if (list is null)
        {
            effectiveDefs = all.Where(d => d.Scope == CustomFieldScope.Workspace).ToList();
        }
        else
        {
            var ancestorFolderIds = await ResolveAncestorFolderIdsAsync(list, ct);
            effectiveDefs = CustomFieldResolution.EffectiveForList(all, list.SpaceId, ancestorFolderIds, list.Id);
        }

        return await ComputeEffectiveValuesAsync(task, effectiveDefs, ct);
    }

    /// <summary>Sets a task's value for a definition, validating the raw value against the field type.</summary>
    public async Task<CustomFieldValueDto> SetValueAsync(Guid taskId, Guid definitionId, string? rawValue, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(task.WorkspaceId, ct))?.Role);

        var definition = await customFields.FindAsync(definitionId, ct)
            ?? throw new NotFoundException("Custom field not found.");
        if (definition.WorkspaceId != task.WorkspaceId)
        {
            throw new ValidationAppException("The custom field does not belong to this task's workspace.");
        }

        if (definition.IsComputed)
        {
            throw new ValidationAppException($"'{definition.Name}' is a computed field and cannot be set directly.");
        }

        if (definition.Type == CustomFieldType.Relationship)
        {
            throw new ValidationAppException($"'{definition.Name}' is a relationship field — use the relationships endpoint to set it.");
        }

        var value = await customFields.FindValueAsync(taskId, definitionId, ct);
        if (value is null)
        {
            value = CustomFieldValue.Create(NewId(), taskId, definitionId);
            customFields.AddValue(value);
        }

        await ApplyTypedValueAsync(task.WorkspaceId, definition, value, rawValue, ct);
        Audit("task.custom_field_set", nameof(CustomFieldValue), value.Id, new { taskId, definitionId });
        await SaveAsync(ct);
        return WorkMapper.ToDto(value);
    }

    /// <summary>Full replacement of a task's linked tasks for one Relationship field.</summary>
    public async Task<CustomFieldValueDto> SetRelationshipValuesAsync(
        Guid taskId, Guid definitionId, SetRelationshipValuesCommand command, CancellationToken ct = default)
    {
        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        WorkManagementAuthorizer.EnsureEditContent((await AccessAsync(task.WorkspaceId, ct))?.Role);

        var definition = await customFields.FindAsync(definitionId, ct)
            ?? throw new NotFoundException("Custom field not found.");
        if (definition.WorkspaceId != task.WorkspaceId)
        {
            throw new ValidationAppException("The custom field does not belong to this task's workspace.");
        }

        if (definition.Type != CustomFieldType.Relationship)
        {
            throw new ValidationAppException($"'{definition.Name}' is not a relationship field.");
        }

        var relatedIds = command.RelatedTaskIds.Distinct().ToList();
        if (relatedIds.Contains(taskId))
        {
            throw new ValidationAppException("A task cannot relate to itself.");
        }

        if (relatedIds.Count > 0)
        {
            var found = await tasks.ListByIdsAsync(relatedIds, ct);
            if (found.Count != relatedIds.Count || found.Any(t => t.WorkspaceId != task.WorkspaceId))
            {
                throw new ValidationAppException("One or more related tasks were not found in this workspace.");
            }
        }

        var existing = await customFields.ListRelationshipValuesAsync(taskId, definitionId, ct);
        foreach (var stale in existing.Where(e => !relatedIds.Contains(e.RelatedTaskId)))
        {
            customFields.RemoveRelationshipValue(stale);
        }

        foreach (var newRelatedId in relatedIds.Where(id => existing.All(e => e.RelatedTaskId != id)))
        {
            customFields.AddRelationshipValue(new CustomFieldRelationshipValue(NewId(), task.WorkspaceId, definitionId, taskId, newRelatedId, Now));
        }

        Audit("task.custom_field_relationship_set", nameof(CustomFieldRelationshipValue), definition.Id, new { taskId, definitionId, relatedIds });
        await SaveAsync(ct);
        return new CustomFieldValueDto(definitionId, null, null, null, null, null, RelatedTaskIds: relatedIds);
    }

    /// <summary>
    /// User is the one type that needs an async workspace-membership check, so it stays here;
    /// every other settable type's parsing/validation is shared with <c>TaskWriteApi.SetCustomFieldValueAsync</c>
    /// (Forms' custom-field mapping) via <see cref="CustomFieldValueCoercion"/> — see that class's doc comment.
    /// </summary>
    private async Task ApplyTypedValueAsync(Guid workspaceId, CustomFieldDefinition definition, CustomFieldValue value, string? rawValue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            value.SetText(null, Now); // clears all projections
            return;
        }

        // References a workspace member. Validated against workspace membership
        // (unlike Team, which is a cross-module opaque id — see CustomFieldType.Team's doc comment).
        if (definition.Type == CustomFieldType.User)
        {
            if (!Guid.TryParse(rawValue, out var userId))
            {
                throw new ValidationAppException($"'{definition.Name}' expects a user id.");
            }

            if (await Ctx.Access.GetAccessAsync(workspaceId, userId, ct) is null)
            {
                throw new ValidationAppException($"'{definition.Name}' must reference a member of this workspace.");
            }

            value.SetUser(userId, Now);
            return;
        }

        CustomFieldValueCoercion.Apply(definition, value, rawValue, Now);
    }

    private async Task<List<Guid>> ResolveAncestorFolderIdsAsync(TaskList list, CancellationToken ct)
    {
        var ancestorFolderIds = new List<Guid>();
        var current = list.FolderId;
        var hops = 0;
        while (current is { } folderId && hops++ < 64)
        {
            ancestorFolderIds.Add(folderId);
            var folder = await folders.FindAsync(folderId, ct);
            current = folder?.ParentFolderId;
        }

        return ancestorFolderIds;
    }

    // ---- formula save-time validation ----

    private static IReadOnlyList<Guid> ResolveFormulaDependencies(
        string fieldName, string? expression, IReadOnlyList<CustomFieldDefinition> siblings, Guid newId)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ValidationAppException("A formula field requires an expression.");
        }

        FormulaNode node;
        try
        {
            node = FormulaParser.Parse(expression);
        }
        catch (FormulaParseException ex)
        {
            throw new ValidationAppException($"'{fieldName}' has an invalid formula: {ex.Message}");
        }

        var dependencyIds = new List<Guid>();
        foreach (var refName in FormulaEvaluator.CollectFieldRefs(node))
        {
            if (string.Equals(refName, "Priority", StringComparison.OrdinalIgnoreCase))
            {
                continue; // built-in, no CustomFieldDefinition backing it, no cycle risk.
            }

            var referenced = siblings.FirstOrDefault(d => string.Equals(d.Name, refName, StringComparison.OrdinalIgnoreCase));
            if (referenced is null || !(NumericTypes.Contains(referenced.Type) || referenced.Type is CustomFieldType.Formula or CustomFieldType.Rollup))
            {
                throw new ValidationAppException($"'{fieldName}' references unknown or non-numeric field '{{{refName}}}'.");
            }

            dependencyIds.Add(referenced.Id);
        }

        // Cycle check: only Formula fields carry same-task dependency edges (Rollup's target is
        // restricted to simple stored types — see CustomFieldDefinition.RollupTargetFieldId).
        var graphNodes = siblings
            .Where(d => d.Type == CustomFieldType.Formula)
            .Select(d => (d.Id, d.FormulaDependencyIds))
            .Append((newId, (IReadOnlyList<Guid>)dependencyIds))
            .ToList();

        if (CustomFieldDependencyGraph.HasCycle(graphNodes))
        {
            throw new ValidationAppException($"'{fieldName}' would create a circular formula dependency.");
        }

        return dependencyIds;
    }

    // ---- rollup save-time validation ----

    private static void ValidateRollupDefinition(CreateCustomFieldCommand command, IReadOnlyList<CustomFieldDefinition> siblings, Guid workspaceId)
    {
        _ = workspaceId;

        if (command.RollupSourceType == CustomFieldRollupSourceType.RelationshipField)
        {
            var sourceField = command.RollupSourceFieldId is { } sourceId ? siblings.FirstOrDefault(d => d.Id == sourceId) : null;
            if (sourceField is null || sourceField.Type != CustomFieldType.Relationship)
            {
                throw new ValidationAppException("The rollup's source field must be an existing Relationship field.");
            }
        }

        if (command.RollupFunction != CustomFieldRollupFunction.Count)
        {
            var targetField = command.RollupTargetFieldId is { } targetId ? siblings.FirstOrDefault(d => d.Id == targetId) : null;
            if (targetField is null || !NumericTypes.Contains(targetField.Type))
            {
                throw new ValidationAppException(
                    "The rollup's target field must be an existing Number, Currency, Rating, Progress or Boolean field.");
            }
        }
    }

    // ---- read-time computation ----

    private async Task<IReadOnlyList<CustomFieldValueDto>> ComputeEffectiveValuesAsync(
        WorkItem task, IReadOnlyList<CustomFieldDefinition> effectiveDefs, CancellationToken ct)
    {
        var stored = (await customFields.ListValuesForTaskAsync(task.Id, ct)).ToDictionary(v => v.DefinitionId);
        var results = new Dictionary<Guid, CustomFieldValueDto>();

        foreach (var def in effectiveDefs.Where(d => !d.IsComputed && d.Type != CustomFieldType.Relationship))
        {
            if (stored.TryGetValue(def.Id, out var v))
            {
                results[def.Id] = WorkMapper.ToDto(v);
            }
        }

        foreach (var def in effectiveDefs.Where(d => d.Type == CustomFieldType.Relationship))
        {
            var related = await customFields.ListRelationshipValuesAsync(task.Id, def.Id, ct);
            if (related.Count > 0)
            {
                results[def.Id] = new CustomFieldValueDto(def.Id, null, null, null, null, null,
                    RelatedTaskIds: related.Select(r => r.RelatedTaskId).ToList());
            }
        }

        var computedDefs = effectiveDefs.Where(d => d.Type is CustomFieldType.Formula or CustomFieldType.Rollup).ToList();
        if (computedDefs.Count == 0)
        {
            return results.Values.ToList();
        }

        var numericValues = BuildNumericValueMap(task, effectiveDefs, stored);
        var order = CustomFieldDependencyGraph.TopologicalOrder(
            computedDefs.Select(d => (d.Id, d.Type == CustomFieldType.Formula ? d.FormulaDependencyIds : (IReadOnlyList<Guid>)[])).ToList());

        foreach (var defId in order)
        {
            var def = computedDefs.First(d => d.Id == defId);
            try
            {
                var value = def.Type == CustomFieldType.Formula
                    ? FormulaEvaluator.Evaluate(FormulaParser.Parse(def.FormulaExpression!), numericValues)
                    : await EvaluateRollupAsync(def, task, ct);

                numericValues[def.Name] = value;
                results[def.Id] = new CustomFieldValueDto(def.Id, null, value, null, null, null);
            }
            catch (Exception ex) when (ex is FormulaParseException or FormulaEvaluationException or RollupEvaluationException)
            {
                results[def.Id] = new CustomFieldValueDto(def.Id, null, null, null, null, null, ComputedError: ex.Message);
            }
        }

        return results.Values.ToList();
    }

    /// <summary>Numeric values available to Formula expressions on this task: the built-in
    /// <c>{Priority}</c> (0-4), and every simple-typed field's stored value (Number/Currency/Rating/
    /// Progress as-is, Boolean as 1/0), keyed by field name case-insensitively.</summary>
    private static Dictionary<string, decimal> BuildNumericValueMap(
        WorkItem task, IReadOnlyList<CustomFieldDefinition> effectiveDefs, Dictionary<Guid, CustomFieldValue> stored)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["Priority"] = (decimal)task.Priority,
        };

        foreach (var def in effectiveDefs.Where(d => NumericTypes.Contains(d.Type)))
        {
            if (!stored.TryGetValue(def.Id, out var v))
            {
                continue;
            }

            if (def.Type == CustomFieldType.Boolean)
            {
                if (v.BoolValue is { } b)
                {
                    map[def.Name] = b ? 1 : 0;
                }
            }
            else if (v.NumberValue is { } n)
            {
                map[def.Name] = n;
            }
        }

        return map;
    }

    /// <summary>
    /// Permission-aware rollup evaluation (option (b) from the design brief): source tasks are
    /// filtered through the SAME per-viewer authorization check (<see cref="WorkServiceBase.CanReadAsync"/>)
    /// used everywhere else in this module, rather than trusting "the field itself is readable" alone —
    /// so a rollup can never surface a value derived from a task the current viewer could not otherwise
    /// browse to. This is more expensive than a single ambient check (one CanReadAsync call per source
    /// task, each up to a private-ancestor probe) but is the correctness-preserving choice given the recurring
    /// leak theme in this area. See the design notes for the perf trade-off discussion.
    /// </summary>
    private async Task<decimal> EvaluateRollupAsync(CustomFieldDefinition def, WorkItem task, CancellationToken ct)
    {
        IReadOnlyList<WorkItem> candidates = def.RollupSourceType == CustomFieldRollupSourceType.Subtasks
            ? await tasks.ListSubtasksAsync(task.Id, ct)
            : await LoadRelationshipTasksAsync(task.Id, def.RollupSourceFieldId!.Value, ct);

        var visible = new List<WorkItem>();
        foreach (var candidate in candidates.Where(c => !c.IsDeleted))
        {
            if (await CanReadAsync(candidate, WorkResourceTypes.Task, ct))
            {
                visible.Add(candidate);
            }
        }

        var values = new List<decimal>();
        if (def.RollupFunction != CustomFieldRollupFunction.Count)
        {
            foreach (var candidate in visible)
            {
                var value = await customFields.FindValueAsync(candidate.Id, def.RollupTargetFieldId!.Value, ct);
                if (value is null)
                {
                    continue;
                }

                if (value.NumberValue is { } n)
                {
                    values.Add(n);
                }
                else if (value.BoolValue is { } b)
                {
                    values.Add(b ? 1 : 0);
                }
            }
        }

        try
        {
            return RollupAggregator.Aggregate(def.RollupFunction!.Value, visible.Count, values);
        }
        catch (RollupEvaluationException ex)
        {
            throw new RollupEvaluationException($"'{def.Name}': {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<WorkItem>> LoadRelationshipTasksAsync(Guid taskId, Guid relationshipDefinitionId, CancellationToken ct)
    {
        var links = await customFields.ListRelationshipValuesAsync(taskId, relationshipDefinitionId, ct);
        if (links.Count == 0)
        {
            return [];
        }

        return await tasks.ListByIdsAsync(links.Select(l => l.RelatedTaskId).ToList(), ct);
    }
}
