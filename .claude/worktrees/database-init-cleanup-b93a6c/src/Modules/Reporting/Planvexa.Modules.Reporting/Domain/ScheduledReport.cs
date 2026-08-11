namespace Planvexa.Modules.Reporting.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

public enum ScheduledReportCadence
{
    Daily = 0,
    Weekly = 1,
}

/// <summary>
/// Net-new: periodically exports a Dashboard and emails it to configured recipients — the
/// background-service side is <c>ScheduledReportBackgroundService</c> (apps/api, mirrors
/// DigestBackgroundService/MissingTimeReminderBackgroundService's exact poll-and-dispatch shape) driving
/// <see cref="Application.Services.ScheduledReportRunner"/> (this class's "what to do for one report"
/// half, testable without a host). Recipients are raw email addresses (pipe-delimited, mirrors
/// FormFieldDefinition.OptionsCsv) — not necessarily workspace members, so they cannot be user ids.
/// </summary>
public sealed class ScheduledReport : Entity, IAggregateRoot, IWorkspaceOwned
{
    private ScheduledReport()
    {
    }

    private ScheduledReport(
        Guid id, Guid workspaceId, Guid dashboardId, string recipientsCsv, ScheduledReportCadence cadence,
        Guid createdByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        DashboardId = dashboardId;
        RecipientsCsv = recipientsCsv;
        Cadence = cadence;
        IsEnabled = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid DashboardId { get; private set; }
    public string RecipientsCsv { get; private set; } = string.Empty;
    public ScheduledReportCadence Cadence { get; private set; }
    public bool IsEnabled { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? LastSentAtUtc { get; private set; }

    public IReadOnlyList<string> Recipients =>
        RecipientsCsv.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static ScheduledReport Create(
        Guid id, Guid workspaceId, Guid dashboardId, IReadOnlyCollection<string> recipients,
        ScheduledReportCadence cadence, Guid createdByUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(dashboardId, nameof(dashboardId));
        var valid = recipients.Where(r => !string.IsNullOrWhiteSpace(r) && r.Contains('@', StringComparison.Ordinal)).Select(r => r.Trim()).Distinct().ToList();
        if (valid.Count == 0)
        {
            throw new ValidationAppException("A scheduled report needs at least one valid recipient email address.");
        }

        return new ScheduledReport(id, workspaceId, dashboardId, string.Join('|', valid), cadence, createdByUserId, nowUtc);
    }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;

    /// <summary>Whether this report is due to send, given the last time it sent (or creation time if never
    /// sent). Mirrors DigestPreference.IsDue's day/week-boundary reasoning: a Daily report is due once
    /// calendar-UTC-day has advanced since the last send; Weekly once 7+ days have elapsed.</summary>
    public bool IsDue(DateTimeOffset nowUtc)
    {
        if (!IsEnabled)
        {
            return false;
        }

        var last = LastSentAtUtc ?? CreatedAtUtc;
        return Cadence switch
        {
            ScheduledReportCadence.Daily => nowUtc.UtcDateTime.Date > last.UtcDateTime.Date,
            ScheduledReportCadence.Weekly => (nowUtc - last).TotalDays >= 7,
            _ => false,
        };
    }

    public void MarkSent(DateTimeOffset nowUtc) => LastSentAtUtc = nowUtc;
}
