namespace Planvexa.Api.Notifications;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Planvexa.Modules.Notifications.Application;
using Planvexa.SharedContracts.Mobile;

/// <summary>
/// Real Web Push sender: encrypts the payload per RFC 8291 and signs an RFC 8292 VAPID JWT (see
/// <see cref="WebPushCrypto"/> for both), then POSTs to each of the recipient's registered push endpoints.
/// A 404/410 response means the push service considers the subscription gone for good, so that device's
/// subscription is cleared (<see cref="IPushDeviceDirectory.MarkPushSubscriptionExpiredAsync"/>) instead of
/// retried. One device's failure never blocks another's -- each subscription is best-effort.
/// Native iOS/Android (FCM/APNs) is still a documented gap: <see cref="IPushDeviceDirectory.ListSubscriptionsAsync"/>
/// only returns devices with a stored Web Push subscription.
/// </summary>
public sealed class WebPushSender(
    IHttpClientFactory httpClientFactory,
    VapidKeyProvider vapidKeys,
    IPushDeviceDirectory pushDevices,
    IConfiguration configuration,
    ILogger<WebPushSender> logger) : IPushSender
{
    public const string ClientName = "WebPush";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SendAsync(Guid workspaceId, Guid recipientUserId, string title, string body, CancellationToken ct = default)
    {
        var subscriptions = await pushDevices.ListSubscriptionsAsync(workspaceId, recipientUserId, ct);
        if (subscriptions.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { title, body }, JsonOptions);
        var client = httpClientFactory.CreateClient(ClientName);
        var subject = configuration["Vapid:Subject"] is { Length: > 0 } configured ? configured : "mailto:admin@planvexa.local";

        foreach (var subscription in subscriptions)
        {
            try
            {
                await SendToSubscriptionAsync(client, subscription, payload, subject, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One dead/unreachable device must not fail the whole delivery -- NotificationDeliveryProcessor
                // marks the delivery Failed (with retry) only if this method throws, so a partial failure across
                // several devices for the same user is swallowed here and logged instead.
                logger.LogWarning(ex, "Web Push delivery failed for device {DeviceId}", subscription.DeviceId);
            }
        }
    }

    private async Task SendToSubscriptionAsync(
        HttpClient client, PushSubscription subscription, byte[] payload, string subject, CancellationToken ct)
    {
        var encryptedBody = WebPushCrypto.Encrypt(payload, subscription.P256dh, subscription.Auth);
        var endpoint = new Uri(subscription.Endpoint);
        var audience = endpoint.GetLeftPart(UriPartial.Authority);
        var jwt = WebPushCrypto.CreateVapidJwt(vapidKeys.Key, audience, subject);
        var publicKey = vapidKeys.PublicKeyBase64Url;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(encryptedBody),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        // RFC 8292 §4: the VAPID public key travels either as the Authorization "k" parameter or the
        // legacy Crypto-Key header -- both are sent for compatibility with older push services.
        request.Headers.TryAddWithoutValidation("Authorization", $"vapid t={jwt}, k={publicKey}");
        request.Headers.TryAddWithoutValidation("Crypto-Key", $"p256ecdsa={publicKey}");
        request.Headers.TryAddWithoutValidation("TTL", "2419200"); // 4 weeks, the common Web Push default.

        var response = await client.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            await pushDevices.MarkPushSubscriptionExpiredAsync(subscription.DeviceId, ct);
        }
    }
}
