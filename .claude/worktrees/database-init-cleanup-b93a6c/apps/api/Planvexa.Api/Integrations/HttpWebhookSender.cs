namespace Planvexa.Api.Integrations;

using Planvexa.SharedContracts.Integrations;

/// <summary>
/// Host implementation of <see cref="IWebhookSender"/> using a pooled <see cref="HttpClient"/>. Sends a
/// signed JSON POST with an <c>X-Planvexa-Signature</c> header. The signature value is produced by
/// <see cref="Planvexa.Modules.Integrations.Domain.SecretCrypto.SignWithTimestamp"/> in the form
/// <c>t=&lt;unix seconds&gt;,v1=&lt;HMAC-SHA256 hex of "{t}.{payload}"&gt;</c> (replay
/// protection) — receivers must recompute <c>v1</c> AND reject deliveries whose <c>t</c> is outside a
/// tolerance window (this codebase's dispatcher documents 5 minutes,
/// see <see cref="Planvexa.Modules.Integrations.Application.Services.WebhookDispatcher.ReplayToleranceSeconds"/>),
/// otherwise a captured payload+signature can be replayed indefinitely. Failures (non-2xx, timeouts,
/// network errors) are returned as an unsuccessful result rather than thrown, so the dispatcher can record
/// the delivery outcome.
/// </summary>
public sealed class HttpWebhookSender(IHttpClientFactory httpClientFactory, ILogger<HttpWebhookSender> logger)
    : IWebhookSender
{
    public const string ClientName = "webhooks";
    public const string SignatureHeader = "X-Planvexa-Signature";

    public async Task<WebhookSendResult> SendAsync(string url, string payload, string signature, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(SignatureHeader, signature);

            using var response = await client.SendAsync(request, cancellationToken);
            var code = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new WebhookSendResult(true, code, null)
                : new WebhookSendResult(false, code, $"Non-success status {code}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Webhook delivery to {Url} failed.", url);
            return new WebhookSendResult(false, null, ex.Message);
        }
    }
}
