namespace Planvexa.Api.Notifications;

using Planvexa.Modules.Notifications.Application;

/// <summary>
/// Development email sender. Logs the email (visible in dev logs / can be pointed at Mailpit later).
/// Records the last sent message per recipient so integration tests can assert delivery without SMTP.
/// Do NOT use in Production — a real provider-backed sender replaces this.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, SentEmailLog sentLog) : IEmailSender, Planvexa.SharedContracts.Notifications.IEmailSender
{
    public Task SendAsync(Guid recipientUserId, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL to {Recipient}: {Subject}", recipientUserId, subject);
        sentLog.Record(recipientUserId, subject, body);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory record of sent emails (dev/test observability).</summary>
public sealed class SentEmailLog
{
    private readonly List<SentEmail> _sent = new();
    private readonly List<SentEmailToAddress> _sentByEmail = new();
    private readonly Lock _gate = new();

    public void Record(Guid recipientUserId, string subject, string body)
    {
        lock (_gate)
        {
            _sent.Add(new SentEmail(recipientUserId, subject, body, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>Records an email addressed to a raw address (e.g. an invitation to a not-yet-user).</summary>
    public void RecordForEmail(string email, string subject, string body)
    {
        lock (_gate)
        {
            _sentByEmail.Add(new SentEmailToAddress(email.Trim().ToLowerInvariant(), subject, body, DateTimeOffset.UtcNow));
        }
    }

    public IReadOnlyList<SentEmail> ForRecipient(Guid recipientUserId)
    {
        lock (_gate)
        {
            return _sent.Where(e => e.RecipientUserId == recipientUserId).ToList();
        }
    }

    public IReadOnlyList<SentEmailToAddress> ForEmail(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        lock (_gate)
        {
            return _sentByEmail.Where(e => e.Email == normalized).ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _sent.Count + _sentByEmail.Count;
            }
        }
    }
}

public sealed record SentEmail(Guid RecipientUserId, string Subject, string Body, DateTimeOffset SentAtUtc);

public sealed record SentEmailToAddress(string Email, string Subject, string Body, DateTimeOffset SentAtUtc);
