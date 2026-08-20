namespace Planvexa.Modules.Reporting.Application.Services;

using System.Text;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Reporting.Authorization;
using Planvexa.Modules.Reporting.Domain;
using Planvexa.SharedContracts.Notifications;

/// <summary>Scheduled-report CRUD. Admin+ (same gate as Portfolio/Dashboard export — a
/// scheduled export can leave the workspace via arbitrary email addresses).</summary>
public sealed class ScheduledReportService(ReportingServiceContext ctx, IScheduledReportStore reports, IDashboardStore dashboards)
    : ReportingServiceBase(ctx)
{
    public async Task<IReadOnlyList<ScheduledReportDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);
        var list = await reports.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<ScheduledReportDto> CreateAsync(CreateScheduledReportCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var dashboard = await dashboards.FindAsync(command.DashboardId, ct);
        if (dashboard is null || dashboard.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Dashboard not found.");
        }

        var report = ScheduledReport.Create(NewId(), workspaceId, command.DashboardId, command.Recipients, command.Cadence, UserId, Now);
        reports.Add(report);
        Audit("reporting.scheduled_report_created", "ScheduledReport", report.Id, new { command.DashboardId, command.Cadence });
        await SaveAsync(ct);
        return ToDto(report);
    }

    public async Task<ScheduledReportDto> SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var report = await reports.FindAsync(workspaceId, id, ct) ?? throw new NotFoundException("Scheduled report not found.");
        report.SetEnabled(enabled);
        Audit("reporting.scheduled_report_toggled", "ScheduledReport", report.Id, new { enabled });
        await SaveAsync(ct);
        return ToDto(report);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        ReportingAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var report = await reports.FindAsync(workspaceId, id, ct) ?? throw new NotFoundException("Scheduled report not found.");
        Audit("reporting.scheduled_report_deleted", "ScheduledReport", report.Id);
        reports.Remove(report);
        await SaveAsync(ct);
    }

    private static ScheduledReportDto ToDto(ScheduledReport r) => new(r.Id, r.DashboardId, r.Recipients, r.Cadence, r.IsEnabled, r.LastSentAtUtc);
}

/// <summary>
/// "What to do for one due ScheduledReport" — split from the background service so it is testable without
/// a host (mirrors DigestRunner/MissingTimeReminderRunner's exact split, per this repo's testability
/// convention). Exports the Dashboard's widget data as CSV (the dependency-free-writer pattern —
/// see FormsXlsxWriter/Forms' internal CsvWriter's doc comments on why this is duplicated per-module
/// rather than shared for one small utility) and emails it via <see cref="IReportEmailSender"/>.
/// Idempotent: only called for reports <see cref="ScheduledReport.IsDue"/> returns true for, and always
/// advances <see cref="ScheduledReport.LastSentAtUtc"/> after a successful send.
/// </summary>
public sealed class ScheduledReportRunner(
    IScheduledReportStore reports, IDashboardStore dashboards, WidgetComputer widgetComputer,
    IReportEmailSender emailSender, IClock clock, IUnitOfWork unitOfWork)
{
    public Task<IReadOnlyList<ScheduledReport>> ListEnabledAsync(CancellationToken ct = default) => reports.ListEnabledAsync(ct);

    public async Task<bool> RunAsync(ScheduledReport report, CancellationToken ct)
    {
        var nowUtc = clock.UtcNow;
        if (!report.IsDue(nowUtc))
        {
            return false;
        }

        var dashboard = await dashboards.FindWithWidgetsAsync(report.DashboardId, ct);
        if (dashboard is null)
        {
            report.MarkSent(nowUtc);
            await unitOfWork.SaveChangesAsync(ct);
            return false;
        }

        var to = nowUtc;
        var from = to.AddDays(-30);
        var rows = new List<IReadOnlyList<string>>();
        foreach (var widget in dashboard.Widgets.OrderBy(w => w.Position))
        {
            var series = await widgetComputer.ComputeAsync(report.WorkspaceId, widget.Type, from, to, nowUtc, widget.ConfigJson, ct);
            foreach (var point in series)
            {
                rows.Add(new[] { widget.Type.ToString(), point.Label, point.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }
        }

        var csv = CsvWriter.Write(new[] { "Widget", "Label", "Value" }, rows);
        var bytes = Encoding.UTF8.GetBytes(csv);

        await emailSender.SendAsync(
            report.Recipients, $"Scheduled report: {dashboard.Name}",
            $"Attached is the scheduled {report.Cadence} export of the \"{dashboard.Name}\" dashboard.",
            bytes, $"{dashboard.Name}.csv", "text/csv", ct);

        report.MarkSent(nowUtc);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>Minimal RFC 4180 CSV writer — no external dependency, mirrors Forms'/Governance's identical
/// internal CsvWriter (not shared cross-module per AGENTS.md rule 7; small enough pure utility to
/// duplicate rather than plumb through SharedContracts for one static method).</summary>
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

            // Neutralize BEFORE the quoting decision: prefixing a value that starts with CR/tab adds a
            // character but leaves the control character in place, so needsQuoting still has to see it.
            var field = Neutralize(fields[i]);
            var needsQuoting = field.Contains(',', StringComparison.Ordinal) || field.Contains('"', StringComparison.Ordinal)
                || field.Contains('\n', StringComparison.Ordinal) || field.Contains('\r', StringComparison.Ordinal);
            if (needsQuoting)
            {
                builder.Append('"').Append(field.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
            }
            else
            {
                builder.Append(field);
            }
        }
    }

    /// <summary>
    /// Defuses spreadsheet formula injection. Excel and Google Sheets EXECUTE a cell whose text begins
    /// with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab or CR, and this module's rows carry
    /// user-authored dashboard widget names and labels. Worth guarding even though the values look
    /// benign: this CSV is EMAILED on a schedule, so a poisoned cell reaches recipients' inboxes
    /// unattended rather than only someone who chose to click export.
    ///
    /// The apostrophe prefix is the conventional fix: spreadsheets read it as "treat this as text" and
    /// do not display it, while a plain CSV parser sees one extra literal character.
    /// </summary>
    private static string Neutralize(string value)
        => value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;
}
