namespace Planvexa.SharedContracts.Integrations;

/// <summary>
/// Contract (implemented by the Integrations module) for the Automations "integration" action: invokes
/// one of the workspace's configured third-party integrations by provider key (see
/// <c>IntegrationProviders</c>). Only providers with a real implementation (Slack, GitHub —
/// <c>IntegrationProviders.RealImplementation</c>) perform an actual outbound call; every other provider
/// returns <c>Success:false</c> with a clear "not implemented" detail rather than a faked success — same
/// no-fake-success contract as <c>ISlackClient</c>/<c>IGitHubClient</c>.
/// </summary>
public interface IIntegrationActionInvoker
{
    /// <param name="provider">One of <c>IntegrationProviders</c>' constants (e.g. "slack", "github").</param>
    /// <param name="message">Slack: the message text. GitHub: the issue comment body.</param>
    /// <param name="issueNumber">GitHub only: the issue number to comment on. Ignored by other providers.</param>
    Task<IntegrationActionResult> InvokeAsync(
        Guid workspaceId, string provider, string message, int? issueNumber, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of an "integration" automation action call — same shape for every provider.</summary>
public sealed record IntegrationActionResult(bool Success, string? Detail);
