namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>Defines a custom field for tasks within a scope (workspace/space/list). ADR-0008.</summary>
public sealed class CustomFieldDefinition : Entity, IWorkspaceOwned
{
    private readonly List<CustomFieldOption> _options = new();

    private CustomFieldDefinition()
    {
    }

    private CustomFieldDefinition(
        Guid id, Guid workspaceId, CustomFieldScope scope, Guid? scopeId,
        string name, CustomFieldType type, bool isRequired, double position)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Scope = scope;
        ScopeId = scopeId;
        Name = name;
        Type = type;
        IsRequired = isRequired;
        Position = position;
    }

    public Guid WorkspaceId { get; private set; }
    public CustomFieldScope Scope { get; private set; }

    /// <summary>Space or list id for scoped fields; null for workspace-wide.</summary>
    public Guid? ScopeId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public CustomFieldType Type { get; private set; }
    public bool IsRequired { get; private set; }
    public double Position { get; private set; }

    /// <summary>The field's expression, e.g. <c>"{Estimate} + {Buffer}"</c>. Only set when
    /// <see cref="Type"/> is <see cref="CustomFieldType.Formula"/>. See FormulaEngine.cs for the parser.</summary>
    public string? FormulaExpression { get; private set; }

    /// <summary>Comma-separated definition ids the formula resolved its <c>{FieldName}</c> references to at
    /// save time (CustomFieldService), used for save-time cycle detection and read-time evaluation
    /// ordering (CustomFieldDependencyGraph) — a plain scalar column rather than a JSON collection so no
    /// EF value-comparer plumbing is needed for what is always read as a whole and never queried into.</summary>
    public string? FormulaDependencyIdsCsv { get; private set; }

    public IReadOnlyList<Guid> FormulaDependencyIds =>
        string.IsNullOrEmpty(FormulaDependencyIdsCsv)
            ? []
            : FormulaDependencyIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();

    /// <summary>Only set when <see cref="Type"/> is <see cref="CustomFieldType.Rollup"/>.</summary>
    public CustomFieldRollupSourceType? RollupSourceType { get; private set; }

    /// <summary>The Relationship-type field id this rollup's tasks come from — required (and only valid)
    /// when <see cref="RollupSourceType"/> is <see cref="CustomFieldRollupSourceType.RelationshipField"/>.</summary>
    public Guid? RollupSourceFieldId { get; private set; }

    /// <summary>
    /// The field to aggregate across source tasks. Deliberately restricted at save time (see
    /// CustomFieldService validation) to simple stored-value types (Number/Currency/Rating/Progress/
    /// Boolean) — NOT another Formula/Rollup field — so cross-task rollup evaluation can never recurse
    /// through a Relationship-field cycle (A rolls up B rolls up A). Null only when
    /// <see cref="RollupFunction"/> is <see cref="CustomFieldRollupFunction.Count"/>.
    /// </summary>
    public Guid? RollupTargetFieldId { get; private set; }

    public CustomFieldRollupFunction? RollupFunction { get; private set; }

    public IReadOnlyList<CustomFieldOption> Options => _options.AsReadOnly();

    public bool IsChoiceType => Type is CustomFieldType.Dropdown or CustomFieldType.MultiSelect;

    /// <summary>Formula/Rollup fields are computed at read time and never have a stored CustomFieldValue row.</summary>
    public bool IsComputed => Type is CustomFieldType.Formula or CustomFieldType.Rollup;

    public static CustomFieldDefinition Create(
        Guid id, Guid workspaceId, CustomFieldScope scope, Guid? scopeId,
        string name, CustomFieldType type, bool isRequired, double position,
        string? formulaExpression = null, IReadOnlyList<Guid>? formulaDependencyIds = null,
        CustomFieldRollupSourceType? rollupSourceType = null, Guid? rollupSourceFieldId = null,
        Guid? rollupTargetFieldId = null, CustomFieldRollupFunction? rollupFunction = null)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        if (scope != CustomFieldScope.Workspace && scopeId is null)
        {
            throw new ValidationAppException("A scoped custom field requires a scope id.");
        }

        if (type == CustomFieldType.Formula)
        {
            if (string.IsNullOrWhiteSpace(formulaExpression))
            {
                throw new ValidationAppException("A formula field requires an expression.");
            }
        }
        else if (formulaExpression is not null)
        {
            throw new ValidationAppException("Only formula fields may have a formula expression.");
        }

        if (type == CustomFieldType.Rollup)
        {
            if (rollupSourceType is null || rollupFunction is null)
            {
                throw new ValidationAppException("A rollup field requires a source and an aggregation function.");
            }

            if (rollupSourceType == CustomFieldRollupSourceType.RelationshipField && rollupSourceFieldId is null)
            {
                throw new ValidationAppException("A relationship-sourced rollup requires the relationship field id.");
            }

            if (rollupSourceType == CustomFieldRollupSourceType.Subtasks && rollupSourceFieldId is not null)
            {
                throw new ValidationAppException("RollupSourceFieldId only applies when the source is a relationship field.");
            }

            if (rollupFunction != CustomFieldRollupFunction.Count && rollupTargetFieldId is null)
            {
                throw new ValidationAppException("This aggregation function requires a target field to aggregate.");
            }

            if (rollupFunction == CustomFieldRollupFunction.Count && rollupTargetFieldId is not null)
            {
                throw new ValidationAppException("Count does not aggregate a target field.");
            }
        }
        else if (rollupSourceType is not null || rollupSourceFieldId is not null || rollupTargetFieldId is not null || rollupFunction is not null)
        {
            throw new ValidationAppException("Only rollup fields may have rollup settings.");
        }

        var definition = new CustomFieldDefinition(id, workspaceId, scope, scopeId, name.Trim(), type, isRequired, position)
        {
            FormulaExpression = formulaExpression,
            RollupSourceType = rollupSourceType,
            RollupSourceFieldId = rollupSourceFieldId,
            RollupTargetFieldId = rollupTargetFieldId,
            RollupFunction = rollupFunction,
        };
        definition.FormulaDependencyIdsCsv = formulaDependencyIds is { Count: > 0 }
            ? string.Join(',', formulaDependencyIds)
            : null;

        return definition;
    }

    public CustomFieldOption AddOption(Guid id, string label, string? color, double position)
    {
        if (!IsChoiceType)
        {
            throw new ValidationAppException("Only dropdown/multi-select fields can have options.");
        }

        var option = CustomFieldOption.Create(id, Id, label, color, position);
        _options.Add(option);
        return option;
    }
}

public sealed class CustomFieldOption : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private CustomFieldOption()
    {
    }

    private CustomFieldOption(Guid id, Guid definitionId, string label, string? color, double position)
        : base(id)
    {
        DefinitionId = definitionId;
        Label = label;
        Color = color;
        Position = position;
    }

    public Guid DefinitionId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public string? Color { get; private set; }
    public double Position { get; private set; }

    public static CustomFieldOption Create(Guid id, Guid definitionId, string label, string? color, double position)
    {
        Guard.AgainstNullOrWhiteSpace(label, nameof(label));
        return new CustomFieldOption(id, definitionId, label.Trim(), color, position);
    }
}

/// <summary>
/// A custom-field value on a task. Stored with typed projection columns (per ADR-0008) so values are
/// indexable/filterable rather than JSON-only. Only the column matching the field type is populated.
/// </summary>
public sealed class CustomFieldValue : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private CustomFieldValue()
    {
    }

    private CustomFieldValue(Guid id, Guid taskId, Guid definitionId)
        : base(id)
    {
        TaskId = taskId;
        DefinitionId = definitionId;
    }

    public Guid TaskId { get; private set; }
    public Guid DefinitionId { get; private set; }

    // Typed projections — exactly one is meaningful per field type.
    public string? TextValue { get; private set; }
    public decimal? NumberValue { get; private set; }
    public DateTimeOffset? DateValue { get; private set; }
    public bool? BoolValue { get; private set; }
    public Guid? OptionId { get; private set; }

    /// <summary>A User-type field's referenced workspace member.</summary>
    public Guid? UserValue { get; private set; }

    /// <summary>A Team-type field's referenced Tenancy Team id (opaque, unvalidated —
    /// see CustomFieldType.Team's doc comment).</summary>
    public Guid? TeamValue { get; private set; }

    /// <summary>Supplemental JSON for multi-select ids or complex metadata (never the sole query model).</summary>
    public string? JsonValue { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static CustomFieldValue Create(Guid id, Guid taskId, Guid definitionId)
        => new(id, taskId, definitionId);

    public void SetText(string? value, DateTimeOffset nowUtc) { Reset(); TextValue = value; UpdatedAtUtc = nowUtc; }
    public void SetNumber(decimal? value, DateTimeOffset nowUtc) { Reset(); NumberValue = value; UpdatedAtUtc = nowUtc; }
    public void SetDate(DateTimeOffset? value, DateTimeOffset nowUtc) { Reset(); DateValue = value; UpdatedAtUtc = nowUtc; }
    public void SetBool(bool? value, DateTimeOffset nowUtc) { Reset(); BoolValue = value; UpdatedAtUtc = nowUtc; }
    public void SetOption(Guid? optionId, DateTimeOffset nowUtc) { Reset(); OptionId = optionId; UpdatedAtUtc = nowUtc; }
    public void SetMultiSelect(string? json, DateTimeOffset nowUtc) { Reset(); JsonValue = json; UpdatedAtUtc = nowUtc; }
    public void SetUser(Guid? userId, DateTimeOffset nowUtc) { Reset(); UserValue = userId; UpdatedAtUtc = nowUtc; }
    public void SetTeam(Guid? teamId, DateTimeOffset nowUtc) { Reset(); TeamValue = teamId; UpdatedAtUtc = nowUtc; }

    /// <summary>Merge: moves this value onto another task (only called when the target has no
    /// value for the same definition yet — see WorkItemService.MergeAsync).</summary>
    public void ReassignTask(Guid newTaskId) => TaskId = newTaskId;

    private void Reset()
    {
        TextValue = null;
        NumberValue = null;
        DateValue = null;
        BoolValue = null;
        OptionId = null;
        JsonValue = null;
        UserValue = null;
        TeamValue = null;
    }
}

/// <summary>
/// A Relationship-type custom field's link from one task to another. A dedicated table
/// keyed by field DEFINITION (unlike the fixed, single <see cref="TaskRelation"/> "relates to"
/// edge) so a workspace can define several differently-named relationship fields (e.g. "Related Epic",
/// "Blocked Deliverable") each with their own set of links on the same task. Directional (task_id -&gt;
/// related_task_id only) — unlike TaskRelation's symmetric pair, a named field like "Blocked Deliverable"
/// does not imply the reverse task also has a reciprocal field pointing back. Workspace-scoped, not
/// restricted to the same list (see CustomFieldType.Relationship's doc comment).
/// </summary>
public sealed class CustomFieldRelationshipValue : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private CustomFieldRelationshipValue()
    {
    }

    public CustomFieldRelationshipValue(Guid id, Guid workspaceId, Guid definitionId, Guid taskId, Guid relatedTaskId, DateTimeOffset createdAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        DefinitionId = definitionId;
        TaskId = taskId;
        RelatedTaskId = relatedTaskId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid DefinitionId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid RelatedTaskId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
