namespace Planvexa.SharedContracts.Notifications;

/// <summary>
/// Automations "email" action: cross-module mirror of the Notifications module's internal
/// <c>IEmailSender</c> (per-workspace-member recipient, no attachment) so other modules can send a plain
/// notification-style email without depending on Notifications internals (AGENTS.md rule 7). The
/// composition root's concrete sender (<c>SmtpEmailSender</c>/<c>LoggingEmailSender</c>) implements both
/// this and the module-private interface — same instance, same delivery path, so email actions get
/// identical behavior (and identical test-time capture via <c>SentEmailLog</c>) as every other in-app
/// email. Restricting the recipient to an existing workspace member (validated by the caller — see
/// AutomationDispatcher's email action) keeps an automation's email action from being used to exfiltrate
/// task details to an arbitrary address.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(Guid recipientUserId, string subject, string body, CancellationToken ct = default);
}
