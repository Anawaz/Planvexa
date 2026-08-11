namespace Planvexa.SharedContracts.Notifications;

/// <summary>
/// Sends a scheduled report export to raw recipient email addresses (not
/// <see cref="Guid"/> user ids — a scheduled report's recipients are workspace-configured addresses,
/// which may not all be members). Deliberately separate from the Notifications module's internal
/// <c>IEmailSender</c> (per-user, no attachment, module-private) so the Reporting module — which cannot
/// reference the Notifications module (AGENTS.md rule 7) — can still send mail; implemented by the API
/// host (composition root) alongside the existing SMTP sender.
/// </summary>
public interface IReportEmailSender
{
    Task SendAsync(
        IReadOnlyCollection<string> recipients, string subject, string body,
        byte[] attachment, string attachmentFileName, string attachmentContentType,
        CancellationToken ct = default);
}
