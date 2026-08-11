namespace Planvexa.Modules.Forms.Domain;

using System.Security.Cryptography;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Forms.Domain.Events;

/// <summary>
/// An intake form attached to a list. Public submissions (resolved by <see cref="PublicToken"/>) create
/// a task in the target list. Only an active form accepts submissions. Owns its fields via the aggregate.
/// Adds branding, spam/rate/submission limits, confirmation pages, and full task-routing
/// (status/priority/tags/due date/team) applied by <c>PublicFormService.SubmitAsync</c> via <c>ITaskWriteApi</c>.
/// </summary>
public sealed class Form : Entity, IAggregateRoot, IWorkspaceOwned
{
    /// <summary>A bot filling and submitting a form in under this many seconds is implausible (/// spam heuristic default; overridable per form via <see cref="MinSubmitSeconds"/>).</summary>
    public const int DefaultMinSubmitSeconds = 2;

    private readonly List<FormField> _fields = new();

    private Form()
    {
    }

    private Form(
        Guid id, Guid workspaceId, Guid listId, string title, string? description,
        string publicToken, Guid createdBy, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ListId = listId;
        Title = title;
        Description = description;
        PublicToken = publicToken;
        IsActive = true;
        CreatedByUserId = createdBy;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ListId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Opaque public identifier used by the anonymous submission endpoint.</summary>
    public string PublicToken { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    // ---- branding ----
    public string? BrandingLogoUrl { get; private set; }
    public string? BrandingColor { get; private set; }

    // ---- confirmation page ----
    public string? ConfirmationMessage { get; private set; }
    public string? ConfirmationRedirectUrl { get; private set; }

    // ---- spam heuristic + submission limits ----
    public int? MinSubmitSeconds { get; private set; }
    public int? MaxTotalSubmissions { get; private set; }
    public int? MaxSubmissionsPerRespondent { get; private set; }

    // ---- full routing beyond the fixed target list ----
    public string? TargetStatusName { get; private set; }
    public string? TargetPriority { get; private set; }
    public string? TargetTagsCsv { get; private set; }
    public Guid? TargetTeamId { get; private set; }

    /// <summary>Opaque cross-module user id (same "unvalidated here, validated by the
    /// target module" pattern as <see cref="TargetTeamId"/>) assigned to the created task —
    /// <c>ITaskWriteApi.AssignAsync</c> itself no-ops if the user isn't a member of the workspace.</summary>
    public Guid? TargetUserId { get; private set; }

    public int? DueDateDaysAfterSubmission { get; private set; }

    public IReadOnlyList<string> TargetTags =>
        (TargetTagsCsv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyList<FormField> Fields => _fields.AsReadOnly();

    public static Form Create(
        Guid id, Guid workspaceId, Guid listId, string title, string? description,
        Guid createdBy, DateTimeOffset nowUtc)
    {
        Guard.AgainstNullOrWhiteSpace(title, nameof(title));
        Guard.AgainstEmpty(listId, nameof(listId));
        return new Form(id, workspaceId, listId, title.Trim(), description, GenerateToken(), createdBy, nowUtc);
    }

    public void Update(string? title, string? description, bool? isActive, DateTimeOffset nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title.Trim();
        }

        if (description is not null)
        {
            Description = description;
        }

        if (isActive.HasValue)
        {
            IsActive = isActive.Value;
        }

        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Updates the extended, all-optional form settings. Every
    /// parameter is a "set to this value, including null/clearing" — unlike <see cref="Update"/>'s
    /// leave-alone-if-null convention — since these are all-or-nothing settings screens client-side.</summary>
    public void UpdateSettings(
        string? brandingLogoUrl, string? brandingColor,
        string? confirmationMessage, string? confirmationRedirectUrl,
        int? minSubmitSeconds, int? maxTotalSubmissions, int? maxSubmissionsPerRespondent,
        string? targetStatusName, string? targetPriority, string? targetTagsCsv, Guid? targetTeamId,
        Guid? targetUserId, int? dueDateDaysAfterSubmission, DateTimeOffset nowUtc)
    {
        BrandingLogoUrl = string.IsNullOrWhiteSpace(brandingLogoUrl) ? null : brandingLogoUrl.Trim();
        BrandingColor = string.IsNullOrWhiteSpace(brandingColor) ? null : brandingColor.Trim();
        ConfirmationMessage = string.IsNullOrWhiteSpace(confirmationMessage) ? null : confirmationMessage.Trim();
        ConfirmationRedirectUrl = string.IsNullOrWhiteSpace(confirmationRedirectUrl) ? null : confirmationRedirectUrl.Trim();
        MinSubmitSeconds = minSubmitSeconds is { } s && s > 0 ? s : null;
        MaxTotalSubmissions = maxTotalSubmissions is { } t && t > 0 ? t : null;
        MaxSubmissionsPerRespondent = maxSubmissionsPerRespondent is { } r && r > 0 ? r : null;
        TargetStatusName = string.IsNullOrWhiteSpace(targetStatusName) ? null : targetStatusName.Trim();
        TargetPriority = string.IsNullOrWhiteSpace(targetPriority) ? null : targetPriority.Trim();
        TargetTagsCsv = string.IsNullOrWhiteSpace(targetTagsCsv)
            ? null
            : string.Join(',', targetTagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        TargetTeamId = targetTeamId;
        TargetUserId = targetUserId;
        DueDateDaysAfterSubmission = dueDateDaysAfterSubmission;
        UpdatedAtUtc = nowUtc;
    }

    public FormField AddField(
        Guid id, string label, FormFieldType type, bool required, IReadOnlyCollection<string> options, int position,
        Guid? conditionFieldId = null, FormFieldConditionOperator? conditionOperator = null, string? conditionValue = null,
        Guid? customFieldDefinitionId = null)
    {
        var field = FormField.Create(id, Id, label, type, required, options, position, conditionFieldId, conditionOperator, conditionValue, customFieldDefinitionId);
        _fields.Add(field);
        return field;
    }

    public void ReplaceFields(IEnumerable<FormFieldSpec> fields, DateTimeOffset nowUtc)
    {
        _fields.Clear();
        foreach (var f in fields)
        {
            _fields.Add(FormField.Create(f.Id, Id, f.Label, f.Type, f.Required, f.Options, f.Position, f.ConditionFieldId, f.ConditionOperator, f.ConditionValue, f.CustomFieldDefinitionId));
        }

        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Builds a task title from submitted values (the first required text-ish field, else the form title).</summary>
    public string BuildTaskTitle(IReadOnlyDictionary<string, string> values)
    {
        var titleField = _fields
            .OrderBy(f => f.Position)
            .FirstOrDefault(f => f.Type is FormFieldType.Text or FormFieldType.LongText);

        if (titleField is not null && values.TryGetValue(titleField.Id.ToString(), out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Length > 200 ? value[..200] : value;
        }

        return Title;
    }

    /// <summary>
    /// The fields currently visible given the submitted values, evaluating each field's
    /// single-level condition against the raw submitted values (a field cannot condition on another
    /// conditional field's resolved visibility — one pass, no dependency graph, documented ceiling below).
    /// </summary>
    public IReadOnlySet<Guid> VisibleFieldIds(IReadOnlyDictionary<string, string> values)
        => _fields.Where(f => IsFieldVisible(f, values)).Select(f => f.Id).ToHashSet();

    /// <summary>Pure, unit-tested: whether <paramref name="field"/>'s visibility condition (if any) is
    /// satisfied by <paramref name="values"/>. A field with no condition is always visible.</summary>
    public static bool IsFieldVisible(FormField field, IReadOnlyDictionary<string, string> values)
    {
        if (field.ConditionFieldId is not { } sourceId || field.ConditionOperator is not { } op)
        {
            return true;
        }

        values.TryGetValue(sourceId.ToString(), out var actual);
        actual ??= string.Empty;
        var expected = field.ConditionValue ?? string.Empty;

        return op switch
        {
            FormFieldConditionOperator.Equals => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            FormFieldConditionOperator.NotEquals => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            FormFieldConditionOperator.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            FormFieldConditionOperator.IsEmpty => string.IsNullOrWhiteSpace(actual),
            FormFieldConditionOperator.IsNotEmpty => !string.IsNullOrWhiteSpace(actual),
            _ => true,
        };
    }

    /// <summary>Validates that all required, currently-VISIBLE fields have a value; a required field
    /// hidden by a condition is never enforced ("validation must not require a hidden field").</summary>
    public void ValidateSubmission(IReadOnlyDictionary<string, string> values)
    {
        foreach (var field in _fields.Where(f => f.Required && IsFieldVisible(f, values)))
        {
            if (!values.TryGetValue(field.Id.ToString(), out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ValidationAppException($"Field '{field.Label}' is required.");
            }
        }
    }

    /// <summary>
    /// Pure spam heuristic — no external CAPTCHA provider is configured in this dev
    /// environment, so a submission is rejected as spam if either (a) the invisible honeypot field was
    /// filled in (real users never see or fill it; bots that blindly fill every field do), or (b) the
    /// elapsed time between the form being rendered and submitted is implausibly short for a human.
    /// ponytail: a timing/honeypot heuristic, not behavioral/ML spam detection — add a CAPTCHA provider
    /// integration if/when this environment has credentials for one.
    /// </summary>
    public bool IsSpamSubmission(string? honeypotValue, DateTimeOffset? renderedAtUtc, DateTimeOffset submittedAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(honeypotValue))
        {
            return true;
        }

        if (renderedAtUtc is { } rendered && submittedAtUtc >= rendered)
        {
            var threshold = TimeSpan.FromSeconds(MinSubmitSeconds ?? DefaultMinSubmitSeconds);
            if (submittedAtUtc - rendered < threshold)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True once the form-wide submission cap has been reached. Pure/unit-tested.</summary>
    public static bool IsOverTotalSubmissionLimit(int totalSoFar, int? maxTotal) => maxTotal is { } m && totalSoFar >= m;

    /// <summary>True once one respondent's own submission cap has been reached. Pure/unit-tested.</summary>
    public static bool IsOverRespondentSubmissionLimit(int respondentSoFar, int? maxPerRespondent) => maxPerRespondent is { } m && respondentSoFar >= m;

    /// <summary>Raised on the <see cref="FormSubmission"/> aggregate, not here — see FormSubmission.Create.</summary>
    private static string GenerateToken()
    {
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexStringLower(buffer);
    }
}

/// <summary>Input shape for <see cref="Form.ReplaceFields"/> — named record instead of a long value-tuple
/// now that fields carry conditional-logic + custom-field-mapping settings.</summary>
public sealed record FormFieldSpec(
    Guid Id, string Label, FormFieldType Type, bool Required, IReadOnlyCollection<string> Options, int Position,
    Guid? ConditionFieldId = null, FormFieldConditionOperator? ConditionOperator = null, string? ConditionValue = null,
    Guid? CustomFieldDefinitionId = null);

/// <summary>A single field on a form. Options apply to <see cref="FormFieldType.Select"/>. There is also an
/// optional show/hide condition on another field's value, and an optional mapping onto a WorkManagement
/// custom field so submissions populate task custom fields, not just the built-in task title/description.</summary>
public sealed class FormField : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private FormField()
    {
    }

    private FormField(
        Guid id, Guid formId, string label, FormFieldType type, bool required, string optionsCsv, int position,
        Guid? conditionFieldId, FormFieldConditionOperator? conditionOperator, string? conditionValue,
        Guid? customFieldDefinitionId)
        : base(id)
    {
        FormId = formId;
        Label = label;
        Type = type;
        Required = required;
        OptionsCsv = optionsCsv;
        Position = position;
        ConditionFieldId = conditionFieldId;
        ConditionOperator = conditionOperator;
        ConditionValue = conditionValue;
        CustomFieldDefinitionId = customFieldDefinitionId;
    }

    public Guid FormId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public FormFieldType Type { get; private set; }
    public bool Required { get; private set; }
    public string OptionsCsv { get; private set; } = string.Empty;
    public int Position { get; private set; }

    /// <summary>The other field on this SAME form whose value this field's visibility
    /// depends on; null means always visible.</summary>
    public Guid? ConditionFieldId { get; private set; }

    public FormFieldConditionOperator? ConditionOperator { get; private set; }
    public string? ConditionValue { get; private set; }

    /// <summary>Opaque id of a WorkManagement <c>CustomFieldDefinition</c> this field's
    /// submitted value should be written onto the created task via <c>ITaskWriteApi.SetCustomFieldValueAsync</c>
    /// — Forms never validates this id itself (cross-module; the target module validates/no-ops on submit,
    /// same pattern as <c>CustomFieldValue.TeamValue</c> being an opaque cross-module id elsewhere).</summary>
    public Guid? CustomFieldDefinitionId { get; private set; }

    public IReadOnlyList<string> Options =>
        OptionsCsv.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static FormField Create(
        Guid id, Guid formId, string label, FormFieldType type, bool required, IReadOnlyCollection<string> options, int position,
        Guid? conditionFieldId = null, FormFieldConditionOperator? conditionOperator = null, string? conditionValue = null,
        Guid? customFieldDefinitionId = null)
    {
        Guard.AgainstNullOrWhiteSpace(label, nameof(label));
        if (conditionFieldId == id)
        {
            throw new ValidationAppException("A field cannot condition its visibility on itself.");
        }

        var optionsCsv = string.Join('|', options.Select(o => o.Trim().Replace("|", "/", StringComparison.Ordinal)).Where(o => o.Length > 0));
        return new FormField(id, formId, label.Trim(), type, required, optionsCsv, position, conditionFieldId, conditionOperator, conditionValue, customFieldDefinitionId);
    }
}
