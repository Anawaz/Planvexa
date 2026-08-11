namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.Modules.Integrations.Domain;
using Planvexa.SharedContracts.Integrations;

/// <summary>
/// Implements <see cref="IIntegrationActionInvoker"/> (the Automations "integration" action) by routing to
/// the one real client for the requested provider — <see cref="ISlackClient"/> or <see cref="IGitHubClient"/>,
/// the only two providers with a real outbound call (<see cref="IntegrationProviders.RealImplementation"/>).
/// Every other provider is settings-scaffolding only (see <see cref="IntegrationProviders"/>'s doc comment),
/// so it returns a clear "not implemented" failure rather than a faked success.
/// </summary>
public sealed class IntegrationActionRunner(ISlackClient slack, IGitHubClient github) : IIntegrationActionInvoker
{
    public async Task<IntegrationActionResult> InvokeAsync(
        Guid workspaceId, string provider, string message, int? issueNumber, CancellationToken cancellationToken = default)
    {
        switch (provider)
        {
            case IntegrationProviders.Slack:
                var slackResult = await slack.PostMessageAsync(workspaceId, message, cancellationToken);
                return new IntegrationActionResult(slackResult.Success, slackResult.Detail);

            case IntegrationProviders.GitHub:
                if (issueNumber is null)
                {
                    return new IntegrationActionResult(false, "GitHub integration action requires an issueNumber.");
                }

                var githubResult = await github.CreateIssueCommentAsync(workspaceId, issueNumber.Value, message, cancellationToken);
                return new IntegrationActionResult(githubResult.Success, githubResult.Detail);

            default:
                return new IntegrationActionResult(false, $"Integration provider '{provider}' has no action implemented yet.");
        }
    }
}
