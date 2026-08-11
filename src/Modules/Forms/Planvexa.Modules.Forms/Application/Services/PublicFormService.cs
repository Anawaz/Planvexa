namespace Planvexa.Modules.Forms.Application.Services;

using System.Security.Cryptography;
using System.Text;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Files;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Forms.Domain;
using Planvexa.SharedContracts.Work;

/// <summary>
/// Handles anonymous public form submission. The workspace is resolved server-side from the form's public
/// token (NEVER from the request body, AGENTS.md rule 5): the form is looked up by token, and the
/// ambient workspace context is bound to the form's workspace (with the system actor) before creating the
/// task and recording the submission. Only an active form accepts submissions; submissions are idempotent
/// per form via an idempotency key.
///
/// Adds: a honeypot + submit-timing spam heuristic, per-form/per-respondent submission caps, full
/// task routing (status/priority/tags/due date/team) and custom-field mapping applied via
/// <see cref="ITaskWriteApi"/>, and a pre-submission file-upload endpoint for File Upload fields. None of
/// this widens what's public — <see cref="GetAsync"/> still returns only the rendering-relevant public
/// projection (builder config like routing/limits/custom-field mappings never leaves FormService, which
/// stays gated behind FormsAuthorizer.EnsureEdit).
/// </summary>
public sealed class PublicFormService(
    IWorkspaceContextAccessor workspaceAccessor,
    IIdGenerator ids,
    IClock clock,
    IFormStore forms,
    IFormSubmissionStore submissions,
    IFormUploadStore uploads,
    IFileStorage storage,
    IMalwareScanner scanner,
    ITaskWriteApi taskWrite,
    IUnitOfWork unitOfWork)
{
    /// <summary>Mirrors WorkManagement's AttachmentService.MaxAttachmentBytes — Forms doesn't depend on
    /// that module, so the limit is restated here (same documented 25 MB ceiling).</summary>
    public const long MaxUploadBytes = 25L * 1024 * 1024;

    public async Task<PublicFormDto> GetAsync(string publicToken, CancellationToken ct)
    {
        var form = await forms.FindByPublicTokenAsync(publicToken, ct);
        if (form is null || !form.IsActive)
        {
            throw new NotFoundException("Form not found.");
        }

        return new PublicFormDto(
            form.Title, form.Description,
            form.Fields.OrderBy(f => f.Position).Select(FormService.ToPublicFieldDto).ToList(),
            form.BrandingLogoUrl, form.BrandingColor, form.ConfirmationMessage, form.ConfirmationRedirectUrl);
    }

    /// <summary>Stores a file for a not-yet-submitted File Upload field. The returned
    /// upload id is what the client sends as that field's value in <see cref="SubmitAsync"/>.</summary>
    public async Task<FormUploadResultDto> UploadFileAsync(
        string publicToken, string? fileName, string? contentType, long sizeBytes, Stream content, CancellationToken ct)
    {
        var form = await forms.FindByPublicTokenAsync(publicToken, ct);
        if (form is null || !form.IsActive)
        {
            throw new NotFoundException("Form not found.");
        }

        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded file is empty.");
        }

        if (sizeBytes > MaxUploadBytes)
        {
            throw new ValidationAppException($"Uploads are limited to {MaxUploadBytes / (1024 * 1024)} MB.");
        }

        var id = ids.NewId();
        var safeName = SanitizeFileName(fileName);
        var storagePath = $"workspaces/{form.WorkspaceId}/forms/{form.Id}/uploads/{id}/{safeName}";
        // Anonymous, unauthenticated upload path — content validation + malware scanning matter here more
        // than anywhere else in this codebase.
        var validatedContent = await FileContentValidator.ValidateAsync(content, safeName, contentType, ct);
        await scanner.EnsureCleanAsync(validatedContent, ct);
        await storage.SaveAsync(storagePath, validatedContent, ct);

        var upload = new FormUpload(
            id, form.WorkspaceId, form.Id, storagePath, safeName,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, sizeBytes, clock.UtcNow);
        uploads.Add(upload);
        await unitOfWork.SaveChangesAsync(ct);

        return new FormUploadResultDto(id, safeName, sizeBytes);
    }

    public async Task<SubmitResultDto> SubmitAsync(
        string publicToken, IReadOnlyDictionary<string, string> values, string? idempotencyKey,
        string? honeypotValue, DateTimeOffset? renderedAtUtc, string? clientIp, CancellationToken ct)
    {
        var form = await forms.FindByPublicTokenAsync(publicToken, ct);
        if (form is null || !form.IsActive)
        {
            throw new NotFoundException("Form not found.");
        }

        var submittedAtUtc = clock.UtcNow;

        // Spam heuristic — reject before touching workspace state at all. Deliberately
        // vague message: don't tell a bot which check it tripped.
        if (form.IsSpamSubmission(honeypotValue, renderedAtUtc, submittedAtUtc))
        {
            throw new ValidationAppException("This submission could not be processed.");
        }

        // A required field hidden by a condition is never enforced, and values for
        // fields that are (self-consistently) hidden given the OTHER submitted values are dropped before
        // validation/persistence/routing — a client can't smuggle a hidden field's value through routing
        // or a custom-field mapping just because it was present in the POST body.
        var visibleIds = form.VisibleFieldIds(values);
        var effectiveValues = values
            .Where(kv => Guid.TryParse(kv.Key, out var fieldId) && visibleIds.Contains(fieldId))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        form.ValidateSubmission(effectiveValues);

        // Bind the ambient workspace to the form's workspace so writes isolate correctly. The actor is the
        // system actor (no interactive user) — task-created events from a form do not loop TASK automations
        // (form.submitted is carved out of that guard — see AutomationDispatcher).
        workspaceAccessor.Set(new WorkspaceContext(
            workspaceId: form.WorkspaceId,
            userId: PlatformActors.System,
            membershipId: null,
            role: string.Empty,
            permissions: new HashSet<string>(),
            entitlements: new HashSet<string>(),
            correlationId: Guid.CreateVersion7().ToString()));

        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.CreateVersion7().ToString() : idempotencyKey.Trim();

        // Idempotency: a repeated submission with the same key returns the original result.
        var existing = await submissions.FindByIdempotencyKeyAsync(form.Id, key, ct);
        if (existing is not null)
        {
            return new SubmitResultDto(existing.Id, existing.CreatedTaskId);
        }

        // Submission caps, enforced AFTER the idempotency check (a retried submission
        // must not consume the cap twice) and BEFORE any task/state is created.
        var respondentKey = HashRespondentKey(clientIp);
        if (Form.IsOverTotalSubmissionLimit(await submissions.CountByFormAsync(form.Id, ct), form.MaxTotalSubmissions))
        {
            throw new ValidationAppException("This form is no longer accepting responses.");
        }

        if (respondentKey is not null
            && Form.IsOverRespondentSubmissionLimit(await submissions.CountByFormAndRespondentAsync(form.Id, respondentKey, ct), form.MaxSubmissionsPerRespondent))
        {
            throw new ValidationAppException("You have already submitted the maximum number of responses for this form.");
        }

        var title = form.BuildTaskTitle(effectiveValues);
        var description = BuildDescription(form, effectiveValues);
        var createdTaskId = await taskWrite.CreateTaskAsync(form.ListId, title, description, ct);

        if (createdTaskId is { } taskId)
        {
            await ApplyRoutingAsync(form, taskId, ct);
            await ApplyFieldMappingsAsync(form, taskId, effectiveValues, ct);
        }

        var submission = FormSubmission.Create(ids.NewId(), form.WorkspaceId, form.Id, createdTaskId, effectiveValues, key, respondentKey, submittedAtUtc);
        submissions.Add(submission);
        await unitOfWork.SaveChangesAsync(ct);

        return new SubmitResultDto(submission.Id, createdTaskId);
    }

    /// <summary>Full routing beyond the fixed target list — status/priority/tags/due
    /// date/team, applied via the same <see cref="ITaskWriteApi"/> surface Automations already uses. Every
    /// call is best-effort (a bad/renamed status or missing team doesn't fail the whole submission).</summary>
    private async Task ApplyRoutingAsync(Form form, Guid taskId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(form.TargetStatusName))
        {
            await taskWrite.SetStatusByNameAsync(taskId, form.TargetStatusName, ct);
        }

        if (!string.IsNullOrWhiteSpace(form.TargetPriority))
        {
            await taskWrite.SetPriorityByNameAsync(taskId, form.TargetPriority, ct);
        }

        foreach (var tag in form.TargetTags)
        {
            await taskWrite.AddTagByNameAsync(taskId, tag, ct);
        }

        if (form.TargetTeamId is { } teamId)
        {
            await taskWrite.AssignTeamAsync(taskId, teamId, ct);
        }

        if (form.TargetUserId is { } userId)
        {
            await taskWrite.AssignAsync(taskId, userId, ct);
        }

        if (form.DueDateDaysAfterSubmission is { } days)
        {
            await taskWrite.SetDueDateAsync(taskId, clock.UtcNow.AddDays(days), ct);
        }
    }

    /// <summary>Writes each mapped field's value onto the task's custom field,
    /// and attaches any File Upload field's stored bytes to the task (no byte copy — same IFileStorage
    /// path is handed straight to WorkManagement).</summary>
    private async Task ApplyFieldMappingsAsync(Form form, Guid taskId, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        foreach (var field in form.Fields)
        {
            if (!values.TryGetValue(field.Id.ToString(), out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (field.Type == FormFieldType.FileUpload)
            {
                if (Guid.TryParse(raw, out var uploadId))
                {
                    var upload = await uploads.FindAsync(uploadId, ct);
                    if (upload is not null && upload.FormId == form.Id)
                    {
                        await taskWrite.AttachFileAsync(taskId, upload.StoragePath, upload.FileName, upload.ContentType, upload.SizeBytes, ct);
                    }
                }

                continue;
            }

            if (field.CustomFieldDefinitionId is { } definitionId)
            {
                await taskWrite.SetCustomFieldValueAsync(taskId, definitionId, raw, ct);
            }
        }
    }

    private static string BuildDescription(Form form, IReadOnlyDictionary<string, string> values)
    {
        var lines = form.Fields
            .OrderBy(f => f.Position)
            .Where(f => f.Type != FormFieldType.FileUpload)
            .Select(f => $"{f.Label}: {(values.TryGetValue(f.Id.ToString(), out var v) ? v : string.Empty)}");
        return "Submitted via form '" + form.Title + "'.\n\n" + string.Join('\n', lines);
    }

    /// <summary>A stable, non-reversible per-respondent key derived from the client IP
    /// (the only identifier available for an anonymous submitter) — hashed, never stored/logged in the
    /// clear. Null when no IP was resolved (e.g. in tests), in which case the per-respondent cap is
    /// simply not enforced for that submission (the form-wide cap still applies).
    /// ponytail: IP-based, so it's imprecise behind shared NAT/VPNs — the design brief explicitly allows
    /// "a signed cookie or IP"; upgrade to a signed cookie if per-respondent precision matters more than
    /// simplicity.</summary>
    private static string? HashRespondentKey(string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(clientIp.Trim()));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Mirrors WorkManagement's AttachmentService.SanitizeFileName (Forms doesn't depend on that
    /// module, so this is restated rather than shared cross-module for one small pure function).</summary>
    private static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        var separator = name.LastIndexOfAny(['/', '\\', ':']);
        if (separator >= 0)
        {
            name = name[(separator + 1)..];
        }

        name = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim('.', ' ');
        if (name.Length > 260)
        {
            name = name[^260..];
        }

        return name.Length == 0 ? "file" : name;
    }
}
