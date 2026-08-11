namespace Planvexa.UnitTests.Integrations;

using System.Net;
using System.Text.Json;
using Planvexa.Api.Integrations;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Domain;
using Shouldly;
using Xunit;

/// <summary>
/// The two fully-implemented third-party integrations (): Slack incoming-webhook
/// message posting and GitHub issue-comment creation. Fully offline — a fake <see cref="HttpMessageHandler"/>
/// stands in for the real endpoint (same pattern as <c>LiteLlmCompletionProviderTests</c>), proving the
/// outbound call shape (URL, headers, JSON body) without touching the network.
/// </summary>
public sealed class SlackClientTests
{
    private static readonly Guid WorkspaceId = Guid.CreateVersion7();

    [Fact]
    public async Task PostMessageAsync_posts_json_text_to_the_configured_webhook_url()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        var settings = Configured(IntegrationProviders.Slack, "{}", "https://hooks.slack.com/services/T000/B000/xyz");
        var client = new SlackClient(new StubFactory(handler), new StubStore(settings), new PlainProtector());

        var result = await client.PostMessageAsync(WorkspaceId, "Hello from Planvexa");

        result.Success.ShouldBeTrue();
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("https://hooks.slack.com/services/T000/B000/xyz");
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        using var body = JsonDocument.Parse(handler.LastBody!);
        body.RootElement.GetProperty("text").GetString().ShouldBe("Hello from Planvexa");
    }

    [Fact]
    public async Task PostMessageAsync_is_a_no_op_when_slack_is_not_configured()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call Slack"));
        var client = new SlackClient(new StubFactory(handler), new StubStore(null), new PlainProtector());

        var result = await client.PostMessageAsync(WorkspaceId, "Hi");

        result.Success.ShouldBeFalse();
        result.Detail.ShouldNotBeNull();
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task PostMessageAsync_surfaces_a_non_success_response()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("invalid_payload") });
        var settings = Configured(IntegrationProviders.Slack, "{}", "https://hooks.slack.com/services/T000/B000/xyz");
        var client = new SlackClient(new StubFactory(handler), new StubStore(settings), new PlainProtector());

        var result = await client.PostMessageAsync(WorkspaceId, "Hi");

        result.Success.ShouldBeFalse();
        result.Detail.ShouldNotBeNull();
        result.Detail!.ShouldContain("400");
    }

    private static IntegrationProviderSettings Configured(string provider, string configJson, string secret)
    {
        var settings = IntegrationProviderSettings.CreateDefault(Guid.CreateVersion7(), WorkspaceId, provider, DateTimeOffset.UtcNow);
        settings.Update(configJson, secret, isEnabled: true, DateTimeOffset.UtcNow);
        return settings;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubStore(IntegrationProviderSettings? settings) : IIntegrationProviderSettingsStore
    {
        public void Add(IntegrationProviderSettings s) => throw new NotSupportedException();

        public Task<IntegrationProviderSettings?> FindAsync(Guid workspaceId, string provider, CancellationToken ct = default)
            => Task.FromResult(settings);

        public Task<IReadOnlyList<IntegrationProviderSettings>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntegrationProviderSettings>>(settings is null ? Array.Empty<IntegrationProviderSettings>() : new[] { settings });
    }

    private sealed class PlainProtector : IIntegrationSecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string protectedValue) => protectedValue;
    }
}

public sealed class GitHubClientTests
{
    private static readonly Guid WorkspaceId = Guid.CreateVersion7();

    [Fact]
    public async Task CreateIssueCommentAsync_posts_to_the_configured_repos_issue_endpoint_with_a_bearer_token()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });
        var settings = Configured("""{"owner":"planvexa","repo":"app"}""", "ghp_secrettoken");
        var client = new GitHubClient(new StubFactory(handler), new StubStore(settings), new PlainProtector());

        var result = await client.CreateIssueCommentAsync(WorkspaceId, 42, "Linked from Planvexa task PLAN-1");

        result.Success.ShouldBeTrue();
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("https://api.github.com/repos/planvexa/app/issues/42/comments");
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.ShouldBe("ghp_secrettoken");

        using var body = JsonDocument.Parse(handler.LastBody!);
        body.RootElement.GetProperty("body").GetString().ShouldBe("Linked from Planvexa task PLAN-1");
    }

    [Fact]
    public async Task CreateIssueCommentAsync_is_a_no_op_without_an_owner_repo_configured()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call GitHub"));
        var settings = Configured("{}", "ghp_secrettoken");
        var client = new GitHubClient(new StubFactory(handler), new StubStore(settings), new PlainProtector());

        var result = await client.CreateIssueCommentAsync(WorkspaceId, 1, "x");

        result.Success.ShouldBeFalse();
        handler.LastRequest.ShouldBeNull();
    }

    private static IntegrationProviderSettings Configured(string configJson, string secret)
    {
        var settings = IntegrationProviderSettings.CreateDefault(Guid.CreateVersion7(), WorkspaceId, IntegrationProviders.GitHub, DateTimeOffset.UtcNow);
        settings.Update(configJson, secret, isEnabled: true, DateTimeOffset.UtcNow);
        return settings;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubStore(IntegrationProviderSettings? settings) : IIntegrationProviderSettingsStore
    {
        public void Add(IntegrationProviderSettings s) => throw new NotSupportedException();

        public Task<IntegrationProviderSettings?> FindAsync(Guid workspaceId, string provider, CancellationToken ct = default)
            => Task.FromResult(settings);

        public Task<IReadOnlyList<IntegrationProviderSettings>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IntegrationProviderSettings>>(settings is null ? Array.Empty<IntegrationProviderSettings>() : new[] { settings });
    }

    private sealed class PlainProtector : IIntegrationSecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string protectedValue) => protectedValue;
    }
}
