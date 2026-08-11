namespace Planvexa.UnitTests;

using System.Net;
using System.Text.Json;
using Planvexa.Api.Ai;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Ai.Application;
using Planvexa.Modules.Ai.Domain;
using Planvexa.SharedContracts.Ai;
using Shouldly;
using Xunit;

/// <summary>
/// Request/response mapping for the per-workspace LiteLLM provider. Fully offline: a fake
/// <see cref="HttpMessageHandler"/> stands in for the endpoint, so no network is touched.
/// </summary>
public sealed class LiteLlmCompletionProviderTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Posts_an_openai_compatible_chat_request_and_returns_the_message_content()
    {
        var handler = new StubHandler(Chat("A tidy summary.", totalTokens: 42));
        var provider = Build(handler, Settings("http://localhost:4000/", "gpt-4o-mini", "sk-secret-1234", enabled: true));

        var result = await provider.CompleteAsync(new AiPrompt(AiTaskKind.Summarize, "Ship it", "With care", ["note"]));

        result.Text.ShouldBe("A tidy summary.");
        result.TokensEstimated.ShouldBe(42);

        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().ShouldBe("http://localhost:4000/chat/completions");
        handler.LastRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.ShouldBe("sk-secret-1234");

        using var body = JsonDocument.Parse(handler.LastBody!);
        body.RootElement.GetProperty("model").GetString().ShouldBe("gpt-4o-mini");
        body.RootElement.GetProperty("temperature").GetDouble().ShouldBe(0.2);
        var messages = body.RootElement.GetProperty("messages");
        messages.GetArrayLength().ShouldBe(2);
        messages[0].GetProperty("role").GetString().ShouldBe("system");
        messages[1].GetProperty("role").GetString().ShouldBe("user");
        var userMessage = messages[1].GetProperty("content").GetString()!;
        userMessage.ShouldContain("Ship it");
        userMessage.ShouldContain("With care");
        userMessage.ShouldContain("note");
    }

    [Fact]
    public async Task Sends_no_authorization_header_when_no_api_key_is_configured()
    {
        var handler = new StubHandler(Chat("ok"));
        var provider = Build(handler, Settings("http://localhost:4000", "local-model", apiKey: string.Empty, enabled: true));

        await provider.CompleteAsync(new AiPrompt(AiTaskKind.Summarize, "T", null, []));

        handler.LastRequest!.Headers.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task Estimates_tokens_when_the_provider_reports_no_usage()
    {
        var handler = new StubHandler(Chat("one two three"));
        var provider = Build(handler, Settings("http://localhost:4000", "m", "k", enabled: true));

        (await provider.CompleteAsync(new AiPrompt(AiTaskKind.Summarize, "T", null, []))).TokensEstimated.ShouldBe(4);
    }

    [Fact]
    public async Task Strips_bullets_and_numbering_from_subtask_lines()
    {
        var handler = new StubHandler(Chat("1. Draft the spec\n- Review it\n* Ship it"));
        var provider = Build(handler, Settings("http://localhost:4000", "m", "k", enabled: true));

        var result = await provider.CompleteAsync(new AiPrompt(AiTaskKind.GenerateSubtasks, "T", null, []));

        result.Text.ShouldBe("Draft the spec\nReview it\nShip it");
    }

    [Fact]
    public async Task Falls_back_to_the_deterministic_provider_when_the_tenant_disabled_the_provider()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call the provider"));
        var provider = Build(handler, Settings("http://localhost:4000", "m", "k", enabled: false));

        var result = await provider.CompleteAsync(new AiPrompt(AiTaskKind.Summarize, "Fix urgent login bug", null, []));

        result.Text.ShouldContain("Fix urgent login bug");
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public void Cannot_enable_the_provider_without_a_base_url_and_model()
    {
        var settings = AiProviderSettings.CreateDefault(Guid.NewGuid(), WorkspaceId, DateTimeOffset.UtcNow);

        Should.Throw<ValidationAppException>(() => settings.Update(string.Empty, "m", null, true, DateTimeOffset.UtcNow));
        Should.Throw<ValidationAppException>(() => settings.Update("http://localhost:4000", string.Empty, null, true, DateTimeOffset.UtcNow));
        Should.Throw<ValidationAppException>(() => settings.Update("not-a-url", "m", null, false, DateTimeOffset.UtcNow));
        settings.IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void Masks_the_api_key_and_keeps_the_stored_one_when_no_new_key_is_supplied()
    {
        var settings = AiProviderSettings.CreateDefault(Guid.NewGuid(), WorkspaceId, DateTimeOffset.UtcNow);
        settings.Update("http://localhost:4000/", "m", "encrypted-sk-abcd", isEnabled: true, DateTimeOffset.UtcNow);
        settings.BaseUrl.ShouldBe("http://localhost:4000"); // trailing slash trimmed
        settings.IsUsable.ShouldBeTrue();

        settings.Update("http://localhost:4000", "m2", null, isEnabled: true, DateTimeOffset.UtcNow);
        settings.ApiKeyEncrypted.ShouldBe("encrypted-sk-abcd");
        settings.Model.ShouldBe("m2");

        AiProviderSettings.Mask(null).ShouldBe(string.Empty);
        AiProviderSettings.Mask("sk-live-9876").ShouldBe("•••9876");
    }

    [Fact]
    public async Task Falls_back_when_the_tenant_has_no_settings_row()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call the provider"));
        var provider = Build(handler, settings: null);

        (await provider.CompleteAsync(new AiPrompt(AiTaskKind.Summarize, "Task", null, []))).Text.ShouldContain("Task");
    }

    [Fact]
    public async Task Surfaces_provider_failures_instead_of_silently_falling_back()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("upstream exploded"),
        });
        var provider = Build(handler, Settings("http://localhost:4000", "m", "k", enabled: true));

        var ex = await Should.ThrowAsync<ExternalServiceException>(
            () => provider.CompleteAsync(new AiPrompt(AiTaskKind.Summarize, "T", null, [])));
        ex.Message.ShouldContain("500");
        ex.Message.ShouldContain("upstream exploded");
    }

    [Fact]
    public async Task Surfaces_unparseable_responses_as_provider_errors()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[]}", System.Text.Encoding.UTF8, "application/json"),
        });
        var provider = Build(handler, Settings("http://localhost:4000", "m", "k", enabled: true));

        await Should.ThrowAsync<ExternalServiceException>(
            () => provider.CompleteAsync(new AiPrompt(AiTaskKind.Summarize, "T", null, [])));
    }

    // ---- redaction before the outbound call ----

    [Fact]
    public async Task Redacts_an_email_before_the_outbound_call_and_reports_it_on_the_completion()
    {
        var handler = new StubHandler(Chat("Got it."));
        var provider = Build(handler, Settings("http://localhost:4000", "m", "k", enabled: true));

        var result = await provider.CompleteAsync(
            new AiPrompt(AiTaskKind.Summarize, "Contact jane.doe@example.com", "Reach out to jane.doe@example.com for the handoff.", []));

        handler.LastBody.ShouldNotBeNull();
        handler.LastBody.ShouldNotContain("jane.doe@example.com");
        handler.LastBody.ShouldContain("[REDACTED_EMAIL]");
        result.RedactedCount.ShouldBeGreaterThanOrEqualTo(2);
        result.RedactedTypes.ShouldNotBeNull();
        result.RedactedTypes!.ShouldContain("email");
    }

    [Fact]
    public async Task Does_not_redact_for_the_offline_fallback_since_it_never_leaves_the_server()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not call the provider"));
        var provider = Build(handler, Settings("http://localhost:4000", "m", "k", enabled: false));

        var result = await provider.CompleteAsync(new AiPrompt(AiTaskKind.Summarize, "Contact jane.doe@example.com", null, []));

        result.RedactedCount.ShouldBe(0);
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task Probe_returns_null_on_success_and_a_message_on_failure()
    {
        var ok = new StubHandler(Chat("OK"));
        (await Build(ok, settings: null).TestAsync("http://localhost:4000", "m", "k")).ShouldBeNull();

        var bad = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("bad key") });
        var error = await Build(bad, settings: null).TestAsync("http://localhost:4000", "m", "k");
        error.ShouldNotBeNull();
        error!.ShouldContain("401");
    }

    // ---- helpers ----

    private static Func<HttpRequestMessage, HttpResponseMessage> Chat(string content, int? totalTokens = null)
    {
        var usage = totalTokens is { } t ? $",\"usage\":{{\"total_tokens\":{t}}}" : string.Empty;
        var json = $"{{\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(content)}}}}}]{usage}}}";
        return _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private static AiProviderSettings Settings(string baseUrl, string model, string apiKey, bool enabled)
    {
        var settings = AiProviderSettings.CreateDefault(Guid.NewGuid(), WorkspaceId, DateTimeOffset.UtcNow);
        // Enabling requires a usable base url + model, so set the values first and toggle after.
        settings.Update(baseUrl, model, apiKey, isEnabled: false, DateTimeOffset.UtcNow);
        if (enabled)
        {
            settings.Update(baseUrl, model, null, isEnabled: true, DateTimeOffset.UtcNow);
        }

        return settings;
    }

    private static LiteLlmCompletionProvider Build(HttpMessageHandler handler, AiProviderSettings? settings)
    {
        var accessor = new WorkspaceContextAccessor();
        accessor.Set(new WorkspaceContext(WorkspaceId, Guid.NewGuid(), null, "Owner", new HashSet<string>(), new HashSet<string>(), "cid"));
        return new LiteLlmCompletionProvider(
            new StubFactory(handler), new StubStore(settings), new PlainProtector(), accessor,
            new DeterministicAiCompletionProvider());
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

    private sealed class StubStore(AiProviderSettings? settings) : IAiProviderSettingsStore
    {
        public void Add(AiProviderSettings s) => throw new NotSupportedException();

        public Task<AiProviderSettings?> FindAsync(Guid workspaceId, CancellationToken ct = default)
            => Task.FromResult(settings);
    }

    /// <summary>Identity protector: encryption is the host's concern, not this provider's.</summary>
    private sealed class PlainProtector : IAiSecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string protectedValue) => protectedValue;
    }
}
