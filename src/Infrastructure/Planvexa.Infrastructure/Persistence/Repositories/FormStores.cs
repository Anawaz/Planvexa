namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Forms.Application;
using Planvexa.Modules.Forms.Domain;

internal sealed class FormStore(PlanvexaDbContext db, MaintenanceConnection maintenance) : IFormStore
{
    public void Add(Form form) => db.Set<Form>().Add(form);

    public void Remove(Form form) => db.Set<Form>().Remove(form);

    public Task<Form?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Form>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Form?> FindWithFieldsAsync(Guid id, CancellationToken ct = default)
        => db.Set<Form>().Include(f => f.Fields).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Form>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Form>().Include(f => f.Fields)
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);

    // Anonymous public submission: no ambient workspace, so the workspace query filter passes all rows. The
    // public token is globally unique; the token itself proves which workspace/form the submission targets.
    public Task<Form?> FindByPublicTokenAsync(string publicToken, CancellationToken ct = default)
        => maintenance.LookupAsync(db, () => db.Set<Form>().Include(f => f.Fields)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.PublicToken == publicToken, ct));
}

internal sealed class FormSubmissionStore(PlanvexaDbContext db) : IFormSubmissionStore
{
    public void Add(FormSubmission submission) => db.Set<FormSubmission>().Add(submission);

    public Task<FormSubmission?> FindByIdempotencyKeyAsync(Guid formId, string idempotencyKey, CancellationToken ct = default)
        => db.Set<FormSubmission>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.FormId == formId && x.IdempotencyKey == idempotencyKey, ct);

    public async Task<IReadOnlyList<FormSubmission>> ListByFormAsync(Guid formId, int max, CancellationToken ct = default)
        => await db.Set<FormSubmission>()
            .Where(x => x.FormId == formId)
            .OrderByDescending(x => x.SubmittedAtUtc).Take(max).ToListAsync(ct);

    // Submission-cap counts, called from the anonymous public submission flow — same
    // IgnoreQueryFilters() pattern as FindByIdempotencyKeyAsync above (an explicit formId filter, already
    // resolved from the form's own workspace, is workspace-correct on its own).
    public Task<int> CountByFormAsync(Guid formId, CancellationToken ct = default)
        => db.Set<FormSubmission>().IgnoreQueryFilters().CountAsync(x => x.FormId == formId, ct);

    public Task<int> CountByFormAndRespondentAsync(Guid formId, string respondentKey, CancellationToken ct = default)
        => db.Set<FormSubmission>().IgnoreQueryFilters()
            .CountAsync(x => x.FormId == formId && x.RespondentKey == respondentKey, ct);
}

/// <summary>Pending file uploads for File Upload fields, resolved anonymously by id at
/// submission time (same trust model as the rest of the public submission flow — the upload id is opaque
/// and scoped to the form it was uploaded against).</summary>
internal sealed class FormUploadStore(PlanvexaDbContext db) : IFormUploadStore
{
    public void Add(FormUpload upload) => db.Set<FormUpload>().Add(upload);

    public Task<FormUpload?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<FormUpload>().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
}
