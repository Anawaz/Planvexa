namespace Planvexa.Api.Integrations;

using System.Net.Http.Json;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Application.Services;
using Planvexa.Modules.Integrations.Domain;

/// <summary>
/// Real implementation of <see cref="ISlackClient"/>: posts a message to a workspace's configured Slack
/// incoming webhook (https://api.slack.com/messaging/webhooks — "Add New Webhook to Workspace" under a
/// Slack App's Incoming Webhooks feature). The webhook URL itself is the bearer credential (anyone who
/// has it can post), so it is stored as this provider's encrypted secret, never logged or echoed back.
/// A no-op when the workspace hasn't configured/enabled Slack (<see cref="IntegrationProviderSettings.IsUsable"/>)
/// — the app works fully with zero integrations configured.
/// </summary>
public sealed class SlackClient(
    IHttpClientFactory httpClientFactory,
    IIntegrationProviderSettingsStore settingsStore,
    IIntegrationSecretProtector protector) : ISlackClient
{
    public const string ClientName = "slack";

    public async Task<ProviderCallResult> PostMessageAsync(Guid workspaceId, string text, CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.FindAsync(workspaceId, IntegrationProviders.Slack, cancellationToken);
        if (settings is not { IsUsable: true })
        {
            return new ProviderCallResult(false, "Slack is not configured for this workspace.");
        }

        var webhookUrl = protector.Unprotect(settings.SecretEncrypted);
        if (webhookUrl.Length == 0)
        {
            return new ProviderCallResult(false, "Slack is not configured for this workspace.");
        }

        var client = httpClientFactory.CreateClient(ClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(webhookUrl, new { text }, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ProviderCallResult(false, $"Could not reach Slack: {ex.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            // Slack's incoming-webhook API returns the literal text "ok" (200) on success.
            return response.IsSuccessStatusCode
                ? new ProviderCallResult(true, null)
                : new ProviderCallResult(false, $"Slack returned {(int)response.StatusCode}: {Excerpt(body)}");
        }
    }

    private static string Excerpt(string body) => body.Length <= 300 ? body : body[..300] + "…";
}
