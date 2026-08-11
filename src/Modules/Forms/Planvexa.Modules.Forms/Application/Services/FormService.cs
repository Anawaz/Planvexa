namespace Planvexa.Modules.Forms.Application.Services;

using System.Text;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Forms.Authorization;
using Planvexa.Modules.Forms.Domain;

/// <summary>Manages form definitions (authoring, Member+) and reads/exports submissions. Submission
/// access stays behind the same Member+ gate as authoring (AGENTS.md security note: a
/// form's public submission endpoint is anonymous by design, but its builder configuration and submitted
/// responses are NOT — see FormsAuthorizer.EnsureEdit).</summary>
public sealed class FormService(
    FormsServiceContext ctx,
    IFormStore forms,
    IFormSubmissionStore submissions)
    : FormsServiceBase(ctx)
{
    public async Task<IReadOnlyList<FormDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        FormsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var list = await forms.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<FormDto> GetAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        FormsAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var form = await LoadInWorkspaceAsync(workspaceId, id, ct);
        return ToDto(form);
    }

    public async Task<FormDto> CreateAsync(CreateFormCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        FormsAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var form = Form.Create(NewId(), workspaceId, command.ListId, command.Title, command.Description, UserId, Now);
        var position = 0;
        foreach (var field in command.Fields)
        {
            form.AddField(
                NewId(), field.Label, ParseType(field.Type), field.Required, field.Options ?? Array.Empty<string>(), field.Position == 0 ? position : field.Position,
                field.ConditionFieldId, ParseConditionOperator(field.ConditionOperator), field.ConditionValue, field.CustomFieldDefinitionId);
            position++;
        }

        forms.Add(form);
        Audit("forms.form.created", "Form", form.Id, new { form.Title, form.ListId });
        await SaveAsync(ct);
        return ToDto(form);
    }

    public async Task<FormDto> UpdateAsync(Guid id, UpdateFormCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        FormsAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var form = await LoadInWorkspaceAsync(workspaceId, id, ct);
        form.Update(command.Title, command.Description, command.IsActive, Now);
        if (command.Fields is not null)
        {
            form.ReplaceFields(
                command.Fields.Select((f, i) => new FormFieldSpec(
                    NewId(), f.Label, ParseType(f.Type), f.Required, (IReadOnlyCollection<string>)(f.Options ?? Array.Empty<string>()), f.Position == 0 ? i : f.Position,
                    f.ConditionFieldId, ParseConditionOperator(f.ConditionOperator), f.ConditionValue, f.CustomFieldDefinitionId)),
                Now);
        }

        Audit("forms.form.updated", "Form", form.Id, new { form.Title, form.IsActive });
        await SaveAsync(ct);
        return ToDto(form);
    }

    /// <summary>The extended settings screen — branding, spam threshold, submission limits,
    /// confirmation page, and full task-routing. Kept separate from <see cref="UpdateAsync"/> so the
    /// simple title/description/fields screen doesn't need to round-trip every setting.</summary>
    public async Task<FormDto> UpdateSettingsAsync(Guid id, UpdateFormSettingsCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        FormsAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var form = await LoadInWorkspaceAsync(workspaceId, id, ct);
        form.UpdateSettings(
            command.BrandingLogoUrl, command.BrandingColor,
            command.ConfirmationMessage, command.ConfirmationRedirectUrl,
            command.MinSubmitSeconds, command.MaxTotalSubmissions, command.MaxSubmissionsPerRespondent,
            command.TargetStatusName, command.TargetPriority, command.TargetTagsCsv, command.TargetTeamId,
            command.TargetUserId, command.DueDateDaysAfterSubmission, Now);

        Audit("forms.form.settings_updated", "Form", form.Id);
        await SaveAsync(ct);
        return ToDto(form);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        FormsAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var form = await LoadInWorkspaceAsync(workspaceId, id, ct);
        forms.Remove(form);
        Audit("forms.form.deleted", "Form", id);
        await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<FormSubmissionDto>> ListSubmissionsAsync(Guid id, CancellationToken ct)
    {
        var (_, list) = await LoadForSubmissionsExportAsync(id, ct);
        return list
            .Select(s => new FormSubmissionDto(s.Id, s.CreatedTaskId, s.SubmittedAtUtc, s.Values()))
            .ToList();
    }

    /// <summary>CSV export of a form's submission history, gated by the same Member+
    /// check as <see cref="ListSubmissionsAsync"/> — never public.</summary>
    public async Task<string> ExportSubmissionsCsvAsync(Guid id, CancellationToken ct)
    {
        var (form, list) = await LoadForSubmissionsExportAsync(id, ct);
        var (header, rows) = BuildExportRows(form, list);
        return CsvWriter.Write(header, rows);
    }

    /// <summary>Excel export of a form's submission history — same access control and
    /// same row data as the CSV export, just packaged as a minimal .xlsx (see FormsXlsxWriter).</summary>
    public async Task<byte[]> ExportSubmissionsXlsxAsync(Guid id, CancellationToken ct)
    {
        var (form, list) = await LoadForSubmissionsExportAsync(id, ct);
        var (header, rows) = BuildExportRows(form, list);
        return FormsXlsxWriter.Write("Submissions", header, rows);
    }

    private async Task<(Form Form, IReadOnlyList<FormSubmission> Submissions)> LoadForSubmissionsExportAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        FormsAuthorizer.EnsureEdit((await AccessAsync(workspaceId, ct))?.Role);

        var form = await forms.FindWithFieldsAsync(id, ct)
            ?? throw new NotFoundException("Form not found.");
        if (form.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Form not found in this workspace.");
        }

        var list = await submissions.ListByFormAsync(id, 5000, ct);
        return (form, list);
    }

    private static (IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) BuildExportRows(Form form, IReadOnlyList<FormSubmission> list)
    {
        var orderedFields = form.Fields.OrderBy(f => f.Position).ToList();
        var header = new List<string> { "Submitted At (UTC)", "Created Task Id" };
        header.AddRange(orderedFields.Select(f => f.Label));

        var rows = list.Select(s =>
        {
            var values = s.Values();
            var row = new List<string> { s.SubmittedAtUtc.ToString("O"), s.CreatedTaskId?.ToString() ?? string.Empty };
            row.AddRange(orderedFields.Select(f => values.TryGetValue(f.Id.ToString(), out var v) ? v : string.Empty));
            return (IReadOnlyList<string>)row;
        }).ToList();

        return (header, rows);
    }

    private async Task<Form> LoadInWorkspaceAsync(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var form = await forms.FindWithFieldsAsync(id, ct)
            ?? throw new NotFoundException("Form not found.");
        if (form.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Form not found in this workspace.");
        }

        return form;
    }

    internal static FormFieldType ParseType(string type)
        => Enum.TryParse<FormFieldType>(type, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ValidationAppException($"Unknown form field type '{type}'.");

    internal static FormFieldConditionOperator? ParseConditionOperator(string? op)
        => string.IsNullOrWhiteSpace(op)
            ? null
            : Enum.TryParse<FormFieldConditionOperator>(op, ignoreCase: true, out var parsed)
                ? parsed
                : throw new ValidationAppException($"Unknown condition operator '{op}'.");

    internal static FormDto ToDto(Form f)
        => new(f.Id, f.ListId, f.Title, f.Description, f.IsActive, f.PublicToken,
            f.Fields.OrderBy(x => x.Position).Select(ToFieldDto).ToList(),
            f.BrandingLogoUrl, f.BrandingColor, f.ConfirmationMessage, f.ConfirmationRedirectUrl,
            f.MinSubmitSeconds, f.MaxTotalSubmissions, f.MaxSubmissionsPerRespondent,
            f.TargetStatusName, f.TargetPriority, f.TargetTags, f.TargetTeamId, f.TargetUserId, f.DueDateDaysAfterSubmission);

    internal static FormFieldDto ToFieldDto(FormField x)
        => new(x.Id, x.Label, x.Type.ToString(), x.Required, x.Options, x.Position,
            x.ConditionFieldId, x.ConditionOperator?.ToString(), x.ConditionValue, x.CustomFieldDefinitionId);

    internal static PublicFormFieldDto ToPublicFieldDto(FormField x)
        => new(x.Id, x.Label, x.Type.ToString(), x.Required, x.Options, x.Position,
            x.ConditionFieldId, x.ConditionOperator?.ToString(), x.ConditionValue);
}

/// <summary>Minimal RFC 4180 CSV writer — no external dependency, mirrors Governance's CsvWriter (not
/// shared cross-module per AGENTS.md rule 7; this is a small enough pure utility to duplicate rather than
/// plumb through SharedContracts for one static method).</summary>
internal static class CsvWriter
{
    public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        AppendRow(builder, header);
        foreach (var row in rows)
        {
            builder.Append("\r\n");
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            AppendField(builder, fields[i]);
        }
    }

    private static void AppendField(StringBuilder builder, string field)
    {
        var value = field ?? string.Empty;
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        if (!mustQuote)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
    }
}
