namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

internal sealed record AiUsageResp(int RequestCount, long TokensEstimated, bool CreditsEnabled, long? CreditLimit);

internal sealed record AiSettingsResp(string BaseUrl, string Model, string ApiKeyMask, bool IsEnabled, bool AiFeaturesEnabled, int? CreditLimit);

/// <summary>
/// <c>AiProviderSettings.CreditLimit</c>: an optional monthly (calendar month, UTC) cap on estimated tokens
/// spent through a workspace's real AI provider. Drives the real-provider dispatch path
/// (<c>LiteLlmCompletionProvider</c>) end to end against a local in-process fake OpenAI-compatible endpoint
/// (<see cref="FakeAiProvider"/>) — never a real LLM — so no network egress or provider cost is incurred.
/// The offline extractive fallback (used by every other AI test in this suite, which configures no
/// provider at all) is never subject to this limit; only a real, cost-incurring provider call is.
/// </summary>
[Collection("api")]
public sealed class AiCreditLimitFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Low_credit_limit_rejects_real_provider_calls_once_monthly_usage_is_exceeded()
    {
        await using var fake = await FakeAiProvider.StartAsync(tokensPerCall: 1000);
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync("Credit limit space");
        var list = await owner.CreateListAsync(space.Id, "Credit limit list");
        var firstTask = await owner.CreateTaskAsync(list.Id, "First task");
        var secondTask = await owner.CreateTaskAsync(list.Id, "Second task");

        var settings = await owner.PutAsJsonAsync("/api/v1/ai/settings", new
        {
            baseUrl = fake.BaseUrl,
            model = "fake-model",
            apiKey = "k",
            isEnabled = true,
            creditLimit = 10,
        });
        settings.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await settings.Content.ReadFromJsonAsync<AiSettingsResp>())!.CreditLimit.ShouldBe(10);

        // First call: usage so far is 0, well under the limit of 10 — goes through to the (fake) real
        // provider and records ~1000 estimated tokens for this workspace this month.
        var first = await owner.PostAsync(new Uri($"/api/v1/ai/tasks/{firstTask.Id}/summarize", UriKind.Relative), null);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        fake.HitCount.ShouldBe(1);

        // GetUsageAsync now reports the workspace's real credit configuration, not the old hardcoded
        // CreditsEnabled=true/CreditLimit=null.
        var usage = await owner.GetFromJsonAsync<AiUsageResp>("/api/v1/ai/usage");
        usage!.CreditsEnabled.ShouldBeTrue();
        usage.CreditLimit.ShouldBe(10);

        // Second call, a DIFFERENT task (so this is not an idempotent replay of the first request): usage
        // (~1000) now exceeds the limit (10), so the real-provider call is rejected with 429 before it
        // ever reaches the fake provider again — the hit count stays at 1.
        var second = await owner.PostAsync(new Uri($"/api/v1/ai/tasks/{secondTask.Id}/summarize", UriKind.Relative), null);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        fake.HitCount.ShouldBe(1);
    }

    [Fact]
    public async Task Null_credit_limit_never_blocks_real_provider_calls()
    {
        await using var fake = await FakeAiProvider.StartAsync(tokensPerCall: 1000);
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync("No limit space");
        var list = await owner.CreateListAsync(space.Id, "No limit list");
        var firstTask = await owner.CreateTaskAsync(list.Id, "First task");
        var secondTask = await owner.CreateTaskAsync(list.Id, "Second task");

        // creditLimit intentionally omitted: the default, unlimited configuration every existing
        // workspace has today.
        var settings = await owner.PutAsJsonAsync("/api/v1/ai/settings", new
        {
            baseUrl = fake.BaseUrl,
            model = "fake-model",
            apiKey = "k",
            isEnabled = true,
        });
        settings.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await settings.Content.ReadFromJsonAsync<AiSettingsResp>())!.CreditLimit.ShouldBeNull();

        var usage = await owner.GetFromJsonAsync<AiUsageResp>("/api/v1/ai/usage");
        usage!.CreditsEnabled.ShouldBeFalse();
        usage.CreditLimit.ShouldBeNull();

        var first = await owner.PostAsync(new Uri($"/api/v1/ai/tasks/{firstTask.Id}/summarize", UriKind.Relative), null);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Well beyond what any low cap would have allowed — still succeeds, matching current (pre-fix)
        // behaviour exactly for every workspace that never sets a limit.
        var second = await owner.PostAsync(new Uri($"/api/v1/ai/tasks/{secondTask.Id}/summarize", UriKind.Relative), null);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        fake.HitCount.ShouldBe(2);
    }
}

/// <summary>
/// Minimal in-process fake OpenAI/LiteLLM-compatible <c>/chat/completions</c> endpoint used to exercise
/// <c>LiteLlmCompletionProvider</c>'s real-provider dispatch path without any network egress or real LLM
/// provider cost. Binds to an OS-assigned loopback port (never a fixed one, so parallel test runs never
/// collide).
/// </summary>
internal sealed class FakeAiProvider : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _hitCount;

    private FakeAiProvider(WebApplication app)
    {
        _app = app;
    }

    public string BaseUrl { get; private set; } = string.Empty;

    public int HitCount => Volatile.Read(ref _hitCount);

    public static async Task<FakeAiProvider> StartAsync(int tokensPerCall)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        var fake = new FakeAiProvider(app);

        app.MapPost("/chat/completions", () =>
        {
            Interlocked.Increment(ref fake._hitCount);
            return Results.Json(new
            {
                choices = new[] { new { message = new { content = "fake summary" } } },
                usage = new { total_tokens = tokensPerCall },
            });
        });

        await app.StartAsync();
        fake.BaseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return fake;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
