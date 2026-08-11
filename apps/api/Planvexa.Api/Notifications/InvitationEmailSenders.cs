namespace Planvexa.Api.Notifications;

using System.Net.Mail;
using Planvexa.Modules.Tenancy.Application;

/// <summary>
/// Builds the invitation email. The accept link points at the web app's <c>/invite/{token}</c> route,
/// which posts the token to <c>POST /api/v1/invitations/{token}/accept</c>. The token is hex, so it is
/// URL-safe as-is.
/// </summary>
internal static class InvitationEmailComposer
{
    public static (string Subject, string Body) Compose(IConfiguration configuration, InvitationEmailMessage message)
    {
        var webBase = (configuration["App:WebBaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
        var acceptUrl = $"{webBase}/invite/{message.RawToken}";
        var subject = $"You're invited to the {message.WorkspaceName} workspace on Planvexa";
        var body =
            $"You have been invited to join the \"{message.WorkspaceName}\" workspace on Planvexa as {message.Role}.\n\n" +
            $"Accept your invitation:\n{acceptUrl}\n\n" +
            $"This link can be used once and expires on {message.ExpiresAtUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC.\n" +
            "If you did not expect this invitation you can safely ignore this email.";
        return (subject, body);
    }
}

/// <summary>Sends invitation emails over plain SMTP (local Mailpit in development).</summary>
public sealed class SmtpInvitationEmailSender(
    IConfiguration configuration,
    ILogger<SmtpInvitationEmailSender> logger,
    SentEmailLog sentLog) : IInvitationEmailSender
{
    public async Task SendInvitationAsync(InvitationEmailMessage message, CancellationToken cancellationToken = default)
    {
        var (subject, body) = InvitationEmailComposer.Compose(configuration, message);
        var from = configuration["Smtp:From"] ?? "no-reply@planvexa.local";
        var host = configuration["Smtp:Host"]!;
        var port = int.TryParse(configuration["Smtp:Port"], out var configured) ? configured : 25;

        using var client = new SmtpClient(host, port);
        using var mail = new MailMessage(from, message.Email, subject, body);
        await client.SendMailAsync(mail, cancellationToken);

        logger.LogInformation("INVITATION EMAIL to <{Address}> for workspace {Workspace}", message.Email, message.WorkspaceId);
        sentLog.RecordForEmail(message.Email, subject, body);
    }
}

/// <summary>
/// Development/test invitation sender. Logs and records to <see cref="SentEmailLog"/> so tests can read
/// the delivered link without SMTP. Do NOT use in Production — a provider-backed sender replaces this.
/// </summary>
public sealed class LoggingInvitationEmailSender(
    IConfiguration configuration,
    ILogger<LoggingInvitationEmailSender> logger,
    SentEmailLog sentLog) : IInvitationEmailSender
{
    public Task SendInvitationAsync(InvitationEmailMessage message, CancellationToken cancellationToken = default)
    {
        var (subject, body) = InvitationEmailComposer.Compose(configuration, message);
        logger.LogInformation("INVITATION EMAIL to <{Address}>: {Subject}", message.Email, subject);
        sentLog.RecordForEmail(message.Email, subject, body);
        return Task.CompletedTask;
    }
}
