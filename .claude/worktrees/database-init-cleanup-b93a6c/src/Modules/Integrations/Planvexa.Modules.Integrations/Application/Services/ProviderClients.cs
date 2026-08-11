namespace Planvexa.Modules.Integrations.Application.Services;

/// <summary>The outcome of a provider call — same shape for every provider client so the calling service
/// (and its tests) don't special-case per provider.</summary>
public sealed record ProviderCallResult(bool Success, string? Detail);

/// <summary>
/// Posts a message to a workspace's configured Slack incoming webhook
/// (https://api.slack.com/messaging/webhooks). Real implementation: <c>Planvexa.Api.Integrations.SlackClient</c>
/// (composition root, uses <c>IHttpClientFactory</c>) reads the workspace's
/// <see cref="IIntegrationProviderSettingsStore"/> row for <c>IntegrationProviders.Slack</c>; a no-op
/// (Success: false, "not configured") when the workspace hasn't configured/enabled Slack — the app works
/// fully with zero integrations configured. A real deployment needs: a Slack "Incoming Webhook" URL
/// (created via a Slack App's Incoming Webhooks feature, https://api.slack.com/apps → Incoming Webhooks →
/// Add New Webhook to Workspace) stored as this provider's secret.
/// </summary>
public interface ISlackClient
{
    Task<ProviderCallResult> PostMessageAsync(Guid workspaceId, string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates a comment on a GitHub issue (https://docs.github.com/en/rest/issues/comments) — the
/// "issue-linking" depth item from . Real implementation:
/// <c>Planvexa.Api.Integrations.GitHubClient</c>. A no-op (Success: false, "not configured") when the
/// workspace hasn't configured/enabled GitHub. A real deployment needs: a GitHub Personal Access Token
/// (fine-grained, scoped to the target repo(s), "Issues: Read and write" permission —
/// https://github.com/settings/personal-access-tokens) stored as this provider's secret, plus the
/// owner/repo stored in the (non-secret) config.
/// </summary>
public interface IGitHubClient
{
    Task<ProviderCallResult> CreateIssueCommentAsync(Guid workspaceId, int issueNumber, string body, CancellationToken cancellationToken = default);
}
