namespace Planvexa.Modules.Forms.Application;

using Planvexa.Modules.Forms.Domain;

public interface IFormStore
{
    void Add(Form form);
    void Remove(Form form);
    Task<Form?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Form?> FindWithFieldsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Form>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Resolves a form by its public token across all workspaces (for anonymous submission). No workspace filter.</summary>
    Task<Form?> FindByPublicTokenAsync(string publicToken, CancellationToken ct = default);
}

public interface IFormSubmissionStore
{
    void Add(FormSubmission submission);
    Task<FormSubmission?> FindByIdempotencyKeyAsync(Guid formId, string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<FormSubmission>> ListByFormAsync(Guid formId, int max, CancellationToken ct = default);

    /// <summary>Total accepted submissions for the form, for the form-wide cap.</summary>
    Task<int> CountByFormAsync(Guid formId, CancellationToken ct = default);

    /// <summary>Accepted submissions from one respondent, for the per-respondent cap.</summary>
    Task<int> CountByFormAndRespondentAsync(Guid formId, string respondentKey, CancellationToken ct = default);
}

/// <summary>Pending file uploads awaiting a submission that references them.</summary>
public interface IFormUploadStore
{
    void Add(FormUpload upload);
    Task<FormUpload?> FindAsync(Guid id, CancellationToken ct = default);
}
