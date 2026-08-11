namespace Planvexa.Modules.Forms.Application;

// ---- DTOs ----
public sealed record FormFieldDto(
    Guid Id, string Label, string Type, bool Required, IReadOnlyList<string> Options, int Position,
    Guid? ConditionFieldId, string? ConditionOperator, string? ConditionValue, Guid? CustomFieldDefinitionId);

public sealed record FormDto(
    Guid Id, Guid ListId, string Title, string? Description, bool IsActive, string PublicToken, IReadOnlyList<FormFieldDto> Fields,
    string? BrandingLogoUrl, string? BrandingColor, string? ConfirmationMessage, string? ConfirmationRedirectUrl,
    int? MinSubmitSeconds, int? MaxTotalSubmissions, int? MaxSubmissionsPerRespondent,
    string? TargetStatusName, string? TargetPriority, IReadOnlyList<string> TargetTags, Guid? TargetTeamId,
    Guid? TargetUserId, int? DueDateDaysAfterSubmission);

/// <summary>The public (anonymous) projection of a form — no internal routing/limit config leaked (that
/// stays workspace-gated), just what the public submission page needs to render: fields (with visibility
/// conditions so the client can show/hide), branding, and the confirmation experience.</summary>
public sealed record PublicFormFieldDto(
    Guid Id, string Label, string Type, bool Required, IReadOnlyList<string> Options, int Position,
    Guid? ConditionFieldId, string? ConditionOperator, string? ConditionValue);

public sealed record PublicFormDto(
    string Title, string? Description, IReadOnlyList<PublicFormFieldDto> Fields,
    string? BrandingLogoUrl, string? BrandingColor, string? ConfirmationMessage, string? ConfirmationRedirectUrl);

public sealed record FormSubmissionDto(Guid Id, Guid? CreatedTaskId, DateTimeOffset SubmittedAtUtc, IReadOnlyDictionary<string, string> Values);

public sealed record SubmitResultDto(Guid SubmissionId, Guid? CreatedTaskId);

public sealed record FormUploadResultDto(Guid UploadId, string FileName, long SizeBytes);

// ---- Field input ----
public sealed record FormFieldInput(
    string Label, string Type, bool Required, IReadOnlyList<string>? Options, int Position,
    Guid? ConditionFieldId = null, string? ConditionOperator = null, string? ConditionValue = null,
    Guid? CustomFieldDefinitionId = null);

// ---- Commands ----
public sealed record CreateFormCommand(Guid ListId, string Title, string? Description, IReadOnlyList<FormFieldInput> Fields);

public sealed record UpdateFormCommand(string? Title, string? Description, bool? IsActive, IReadOnlyList<FormFieldInput>? Fields);

/// <summary>The extended, all-optional settings screen (branding/spam/limits/confirmation/routing).</summary>
public sealed record UpdateFormSettingsCommand(
    string? BrandingLogoUrl, string? BrandingColor,
    string? ConfirmationMessage, string? ConfirmationRedirectUrl,
    int? MinSubmitSeconds, int? MaxTotalSubmissions, int? MaxSubmissionsPerRespondent,
    string? TargetStatusName, string? TargetPriority, string? TargetTagsCsv, Guid? TargetTeamId,
    Guid? TargetUserId, int? DueDateDaysAfterSubmission);
