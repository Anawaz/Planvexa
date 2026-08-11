namespace Planvexa.Api.Integrations;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Application.Services;
using Planvexa.Modules.Integrations.Domain;

/// <summary>
/// Real implementation of <see cref="IGitHubClient"/>: creates a comment on a GitHub issue via the REST
/// API (https://docs.github.com/en/rest/issues/comments#create-an-issue-comment —
/// <c>POST /repos/{owner}/{repo}/issues/{issue_number}/comments</c>). The owner/repo are non-secret
/// (stored in <see cref="IntegrationProviderSettings.ConfigJson"/> as <c>{"owner":"...","repo":"..."}</c>);
/// the personal access token is the encrypted secret. A no-op when the workspace hasn't configured/
/// enabled GitHub or is missing owner/repo — the app works fully with zero integrations configured.
/// </summary>
public sealed class GitHubClient(
    IHttpClientFactory httpClientFactory,
    IIntegrationProviderSettingsStore settingsStore,
    IIntegrationSecretProtector protector) : IGitHubClient
{
    public const string ClientName = "github";
    private const string ApiBaseUrl = "https://api.github.com";

    public async Task<ProviderCallResult> CreateIssueCommentAsync(Guid workspaceId, int issueNumber, string body, CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.FindAsync(workspaceId, IntegrationProviders.GitHub, cancellationToken);
        if (settings is not { IsUsable: true })
        {
            return new ProviderCallResult(false, "GitHub is not configured for this workspace.");
        }

        var (owner, repo) = ParseConfig(settings.ConfigJson);
        if (owner.Length == 0 || repo.Length == 0)
        {
            return new ProviderCallResult(false, "GitHub is not configured with a repository (owner/repo) for this workspace.");
        }

        var token = protector.Unprotect(settings.SecretEncrypted);
        if (token.Length == 0)
        {
            return new ProviderCallResult(false, "GitHub is not configured for this workspace.");
        }

        var client = httpClientFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{ApiBaseUrl}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{issueNumber}/comments")
        {
            Content = JsonContent.Create(new { body }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Planvexa/1.0");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new ProviderCallResult(false, $"Could not reach GitHub: {ex.Message}");
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return response.IsSuccessStatusCode
                ? new ProviderCallResult(true, null)
                : new ProviderCallResult(false, $"GitHub returned {(int)response.StatusCode}: {Excerpt(responseBody)}");
        }
    }

    private static (string Owner, string Repo) ParseConfig(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var owner = doc.RootElement.TryGetProperty("owner", out var o) ? o.GetString() ?? string.Empty : string.Empty;
            var repo = doc.RootElement.TryGetProperty("repo", out var r) ? r.GetString() ?? string.Empty : string.Empty;
            return (owner.Trim(), repo.Trim());
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty);
        }
    }

    private static string Excerpt(string body) => body.Length <= 300 ? body : body[..300] + "…";
}
