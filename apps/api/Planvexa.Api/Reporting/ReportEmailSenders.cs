namespace Planvexa.Api.Reporting;

using System.Net.Mail;
using Planvexa.Api.Notifications;
using Planvexa.SharedContracts.Notifications;

/// <summary>
/// Sends scheduled-report exports over plain SMTP (local Mailpit in development) — mirrors
/// SmtpInvitationEmailSender's shape but implements <see cref="IReportEmailSender"/> (raw recipient
/// addresses + an attachment) instead of the Notifications module's per-user, attachment-less
/// <c>IEmailSender</c>, since the Reporting module cannot reference the Notifications module
/// (AGENTS.md rule 7) and a scheduled report's recipients are workspace-configured addresses, not
/// necessarily user ids.
/// </summary>
public sealed class SmtpReportEmailSender(
    IConfiguration configuration, ILogger<SmtpReportEmailSender> logger, SentEmailLog sentLog) : IReportEmailSender
{
    public async Task SendAsync(
        IReadOnlyCollection<string> recipients, string subject, string body,
        byte[] attachment, string attachmentFileName, string attachmentContentType, CancellationToken ct = default)
    {
        if (recipients.Count == 0)
        {
            logger.LogWarning("Skipping report email {Subject}: no recipients.", subject);
            return;
        }

        var from = configuration["Smtp:From"] ?? "no-reply@planvexa.local";
        var host = configuration["Smtp:Host"]!;
        var port = int.TryParse(configuration["Smtp:Port"], out var configured) ? configured : 25;

        using var client = new SmtpClient(host, port);
        using var message = new MailMessage { From = new MailAddress(from), Subject = subject, Body = body };
        foreach (var recipient in recipients)
        {
            message.To.Add(recipient);
        }

        using var stream = new MemoryStream(attachment);
        using var mailAttachment = new Attachment(stream, attachmentFileName, attachmentContentType);
        message.Attachments.Add(mailAttachment);

        await client.SendMailAsync(message, ct);

        logger.LogInformation("REPORT EMAIL to {Recipients}: {Subject}", string.Join(',', recipients), subject);
        foreach (var recipient in recipients)
        {
            sentLog.RecordForEmail(recipient, subject, body);
        }
    }
}

/// <summary>
/// Development/test scheduled-report sender. Logs and records to <see cref="SentEmailLog"/> (by raw
/// address) so tests can assert delivery without SMTP — mirrors LoggingInvitationEmailSender. Do NOT use
/// in Production — a provider-backed sender replaces this.
/// </summary>
public sealed class LoggingReportEmailSender(ILogger<LoggingReportEmailSender> logger, SentEmailLog sentLog) : IReportEmailSender
{
    public Task SendAsync(
        IReadOnlyCollection<string> recipients, string subject, string body,
        byte[] attachment, string attachmentFileName, string attachmentContentType, CancellationToken ct = default)
    {
        logger.LogInformation("REPORT EMAIL to {Recipients}: {Subject} ({Bytes} byte attachment {FileName})",
            string.Join(',', recipients), subject, attachment.Length, attachmentFileName);
        foreach (var recipient in recipients)
        {
            sentLog.RecordForEmail(recipient, subject, body);
        }

        return Task.CompletedTask;
    }
}
