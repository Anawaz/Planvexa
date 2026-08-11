namespace Planvexa.Api.Notifications;

using System.Net.Mail;
using Planvexa.Modules.Notifications.Application;
using Planvexa.SharedContracts.Users;

/// <summary>
/// Sends notification emails over plain SMTP (local Mailpit in development).
/// ponytail: stdlib SmtpClient is enough for an unauthenticated local sink; production swaps a
/// provider-backed sender (SES/SendGrid/etc.) behind <see cref="IEmailSender"/> with no caller change.
/// Still records into <see cref="SentEmailLog"/> so dev observability and tests keep working.
/// </summary>
public sealed class SmtpEmailSender(
    IConfiguration configuration,
    IUserDirectory users,
    ILogger<SmtpEmailSender> logger,
    SentEmailLog sentLog) : IEmailSender, Planvexa.SharedContracts.Notifications.IEmailSender
{
    public async Task SendAsync(Guid recipientUserId, string subject, string body, CancellationToken ct = default)
    {
        var recipient = await users.FindByIdAsync(recipientUserId, ct);
        if (recipient is null || string.IsNullOrWhiteSpace(recipient.Email))
        {
            logger.LogWarning("Skipping email {Subject}: no address for recipient {Recipient}", subject, recipientUserId);
            return;
        }

        var from = configuration["Smtp:From"] ?? "no-reply@planvexa.local";
        var host = configuration["Smtp:Host"]!;
        var port = int.TryParse(configuration["Smtp:Port"], out var configured) ? configured : 25;

        using var client = new SmtpClient(host, port);
        using var message = new MailMessage(from, recipient.Email, subject, body);
        await client.SendMailAsync(message, ct);

        logger.LogInformation("EMAIL to {Recipient} <{Address}>: {Subject}", recipientUserId, recipient.Email, subject);
        sentLog.Record(recipientUserId, subject, body);
    }
}
