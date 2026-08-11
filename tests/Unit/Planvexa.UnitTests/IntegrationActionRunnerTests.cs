namespace Planvexa.UnitTests.Integrations;

using Planvexa.Modules.Integrations.Application.Services;
using Planvexa.Modules.Integrations.Domain;
using Shouldly;
using Xunit;

/// <summary>
/// Unit-tests <see cref="IntegrationActionRunner"/> — the Automations "integration" action's routing
/// logic (see AutomationDispatcher.ApplyIntegrationAsync). Slack/GitHub are faked here (their real HTTP
/// behavior is covered by SlackClientTests/GitHubClientTests); this only proves the runner calls the
/// right client with the right arguments, and refuses to fake success for an unimplemented provider.
/// </summary>
public sealed class IntegrationActionRunnerTests
{
    private static readonly Guid WorkspaceId = Guid.CreateVersion7();

    [Fact]
    public async Task InvokeAsync_routes_slack_to_PostMessageAsync()
    {
        var slack = new FakeSlackClient(new ProviderCallResult(true, null));
        var runner = new IntegrationActionRunner(slack, new FakeGitHubClient(new ProviderCallResult(false, null)));

        var result = await runner.InvokeAsync(WorkspaceId, IntegrationProviders.Slack, "Hello team", issueNumber: null);

        result.Success.ShouldBeTrue();
        slack.LastWorkspaceId.ShouldBe(WorkspaceId);
        slack.LastMessage.ShouldBe("Hello team");
    }

    [Fact]
    public async Task InvokeAsync_routes_github_to_CreateIssueCommentAsync()
    {
        var github = new FakeGitHubClient(new ProviderCallResult(true, null));
        var runner = new IntegrationActionRunner(new FakeSlackClient(new ProviderCallResult(false, null)), github);

        var result = await runner.InvokeAsync(WorkspaceId, IntegrationProviders.GitHub, "Linked from Planvexa", issueNumber: 42);

        result.Success.ShouldBeTrue();
        github.LastWorkspaceId.ShouldBe(WorkspaceId);
        github.LastIssueNumber.ShouldBe(42);
        github.LastBody.ShouldBe("Linked from Planvexa");
    }

    [Fact]
    public async Task InvokeAsync_fails_github_without_an_issue_number()
    {
        var runner = new IntegrationActionRunner(new FakeSlackClient(new ProviderCallResult(true, null)), new FakeGitHubClient(new ProviderCallResult(true, null)));

        var result = await runner.InvokeAsync(WorkspaceId, IntegrationProviders.GitHub, "no issue number", issueNumber: null);

        result.Success.ShouldBeFalse();
        result.Detail.ShouldNotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_never_fakes_success_for_an_unimplemented_provider()
    {
        var runner = new IntegrationActionRunner(new FakeSlackClient(new ProviderCallResult(true, null)), new FakeGitHubClient(new ProviderCallResult(true, null)));

        var result = await runner.InvokeAsync(WorkspaceId, IntegrationProviders.GitLab, "does nothing", issueNumber: null);

        result.Success.ShouldBeFalse();
        result.Detail.ShouldNotBeNull();
        result.Detail.ShouldContain("gitlab");
    }

    private sealed class FakeSlackClient(ProviderCallResult result) : ISlackClient
    {
        public Guid LastWorkspaceId { get; private set; }
        public string? LastMessage { get; private set; }

        public Task<ProviderCallResult> PostMessageAsync(Guid workspaceId, string text, CancellationToken cancellationToken = default)
        {
            LastWorkspaceId = workspaceId;
            LastMessage = text;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeGitHubClient(ProviderCallResult result) : IGitHubClient
    {
        public Guid LastWorkspaceId { get; private set; }
        public int LastIssueNumber { get; private set; }
        public string? LastBody { get; private set; }

        public Task<ProviderCallResult> CreateIssueCommentAsync(Guid workspaceId, int issueNumber, string body, CancellationToken cancellationToken = default)
        {
            LastWorkspaceId = workspaceId;
            LastIssueNumber = issueNumber;
            LastBody = body;
            return Task.FromResult(result);
        }
    }
}
