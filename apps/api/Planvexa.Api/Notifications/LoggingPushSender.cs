namespace Planvexa.Api.Notifications;

using Planvexa.Modules.Notifications.Application;

/// <summary>
/// Test/CI push sender: logs the push and records it for test observability, exactly mirroring
/// <see cref="LoggingEmailSender"/>. Web Push delivery itself is no longer a gap -- see
/// <see cref="WebPushSender"/> (RFC 8291/8292, stdlib-only <c>System.Security.Cryptography</c>). Program.cs
/// registers <see cref="WebPushSender"/> in Development and keeps this class everywhere else (the
/// <c>Testing</c> environment's integration tests assert delivery via <see cref="SentPushLog"/> instead of
/// standing up a real browser push endpoint), mirroring how <see cref="SmtpEmailSender"/> replaces
/// <see cref="LoggingEmailSender"/>.
/// Native iOS/Android still needs APNs (an Apple developer certificate/key) or FCM (a Firebase server key)
/// credentials this codebase does not have -- <see cref="WebPushSender"/> only reaches Web Push
/// subscriptions (<see cref="Planvexa.Modules.Mobile.Domain.DeviceRegistration.PushEndpoint"/> non-null).
/// </summary>
public sealed class LoggingPushSender(ILogger<LoggingPushSender> logger, SentPushLog sentLog) : IPushSender
{
    public Task SendAsync(Guid workspaceId, Guid recipientUserId, string title, string body, CancellationToken ct = default)
    {
        logger.LogInformation("PUSH to {Recipient}: {Title}", recipientUserId, title);
        sentLog.Record(recipientUserId, title, body);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory record of sent pushes (dev/test observability), mirroring <see cref="SentEmailLog"/>.</summary>
public sealed class SentPushLog
{
    private readonly List<SentPush> _sent = new();
    private readonly Lock _gate = new();

    public void Record(Guid recipientUserId, string title, string body)
    {
        lock (_gate)
        {
            _sent.Add(new SentPush(recipientUserId, title, body, DateTimeOffset.UtcNow));
        }
    }

    public IReadOnlyList<SentPush> ForRecipient(Guid recipientUserId)
    {
        lock (_gate)
        {
            return _sent.Where(p => p.RecipientUserId == recipientUserId).ToList();
        }
    }
}

public sealed record SentPush(Guid RecipientUserId, string Title, string Body, DateTimeOffset SentAtUtc);
