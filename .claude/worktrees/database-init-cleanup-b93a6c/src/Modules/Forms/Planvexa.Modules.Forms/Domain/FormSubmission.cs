namespace Planvexa.Modules.Forms.Domain;

using System.Text.Json;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.Modules.Forms.Domain.Events;

/// <summary>
/// An immutable record of a form submission. Stores the submitted values as JSON and the id of the task
/// it created. Idempotent per form via <see cref="IdempotencyKey"/> — a repeated submission with the
/// same key returns the original without creating a duplicate task. Raises
/// <see cref="FormSubmittedIntegrationEvent"/> so a workspace's automations can react.
/// </summary>
public sealed class FormSubmission : Entity, IWorkspaceOwned
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private FormSubmission()
    {
    }

    private FormSubmission(
        Guid id, Guid workspaceId, Guid formId, Guid? createdTaskId,
        string valuesJson, string idempotencyKey, string? respondentKey, DateTimeOffset submittedAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        FormId = formId;
        CreatedTaskId = createdTaskId;
        ValuesJson = valuesJson;
        IdempotencyKey = idempotencyKey;
        RespondentKey = respondentKey;
        SubmittedAtUtc = submittedAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid FormId { get; private set; }
    public Guid? CreatedTaskId { get; private set; }
    public string ValuesJson { get; private set; } = "{}";
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>A stable-ish, non-reversible identifier for the anonymous respondent
    /// (a hash of their client IP — see PublicFormService.HashRespondentKey) used to enforce
    /// <see cref="Form.MaxSubmissionsPerRespondent"/>. Null when the client IP was unavailable.</summary>
    public string? RespondentKey { get; private set; }

    public DateTimeOffset SubmittedAtUtc { get; private set; }

    public static FormSubmission Create(
        Guid id, Guid workspaceId, Guid formId, Guid? createdTaskId,
        IReadOnlyDictionary<string, string> values, string idempotencyKey, string? respondentKey, DateTimeOffset nowUtc)
    {
        var submission = new FormSubmission(
            id, workspaceId, formId, createdTaskId,
            JsonSerializer.Serialize(values, JsonOptions), idempotencyKey, respondentKey, nowUtc);
        submission.Raise(new FormSubmittedIntegrationEvent(workspaceId, formId, id, createdTaskId));
        return submission;
    }

    public IReadOnlyDictionary<string, string> Values()
        => JsonSerializer.Deserialize<Dictionary<string, string>>(ValuesJson, JsonOptions) ?? new Dictionary<string, string>();
}
