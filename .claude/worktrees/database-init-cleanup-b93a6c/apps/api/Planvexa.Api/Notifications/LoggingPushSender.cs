namespace Planvexa.Api.Notifications;

using Planvexa.Modules.Notifications.Application;

/// <summary>
/// Development/only push sender: logs the push and records it for test observability, exactly mirroring
/// <see cref="LoggingEmailSender"/>. This is a DOCUMENTED GAP, not a placeholder pretending to work: no
/// real push delivery happens.
///
/// Storage is no longer the gap: <see cref="Planvexa.Modules.Mobile.Domain.DeviceRegistration"/> now
/// stores the browser PushSubscription's raw <c>endpoint</c>/<c>p256dh</c>/<c>auth</c> fields (alongside
/// the pre-existing SHA-256 <c>TokenHash</c>, which stays hashed for dedup lookups only), and a per-process
/// VAPID keypair is exposed at <c>GET /api/v1/mobile/push/vapid-public-key</c> (see
/// <see cref="VapidKeyProvider"/>'s doc comment) for the frontend's <c>PushManager.subscribe()</c> call.
/// What is still missing to make this a real sender:
///  - RFC 8291 payload encryption (AES-128-GCM over an ECDH-derived key using the stored p256dh/auth) —
///    doable with stdlib <c>System.Security.Cryptography</c> (ECDiffieHellman/HKDF/AesGcm) only, no new
///    package.
///  - RFC 8292 VAPID JWT signing of the push request with <see cref="VapidKeyProvider"/>'s private key
///    (also stdlib-only, via <c>ECDsa.SignData</c>) and the resulting <c>Authorization: vapid</c> header.
///  - Native iOS/Android still needs APNs (an Apple developer certificate/key) or FCM (a Firebase server
///    key) credentials this dev environment does not have.
/// Swapping this for a real sender is a one-line DI change in Program.cs behind <see cref="IPushSender"/>,
/// the same way <see cref="SmtpEmailSender"/> replaces <see cref="LoggingEmailSender"/>.
/// </summary>
public sealed class LoggingPushSender(ILogger<LoggingPushSender> logger, SentPushLog sentLog) : IPushSender
{
    public Task SendAsync(Guid recipientUserId, string title, string body, CancellationToken ct = default)
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
