namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Response shapes for the OAuth endpoints.
internal sealed record CreatedOAuthApplicationResp(
    Guid Id, string Name, string ClientId, string ClientSecret, List<string> RedirectUris, List<string> AllowedScopes, DateTimeOffset CreatedAtUtc);
internal sealed record OAuthAuthorizeResp(string Code, string RedirectUri);
internal sealed record OAuthTokenResp(string AccessToken, string TokenType, int ExpiresInSeconds, string? RefreshToken, string Scope);

/// <summary>
/// OAuth2 authorization-code flow end to end (): create an application, authorize it
/// under a real user session, exchange the code for a token via an unauthenticated client (the
/// third-party app's backend), then use the token to call scoped API endpoints. Proves the two security
/// requirements the design brief calls out explicitly: a scoped token cannot exceed its granted scopes,
/// and an OAuth application/token is workspace-isolated exactly like a personal access token.
/// </summary>
[Collection("api")]
public sealed class OAuthFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Authorization_code_flow_issues_a_token_that_can_read_a_task_it_was_scoped_for()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "OAuth-visible task");

        var app = await CreateAppAsync(client, new[] { "tasks:read" });

        var code = await AuthorizeAsync(client, app, new[] { "tasks:read" });
        var token = await ExchangeCodeAsync(app, code, redirectUri: "https://example.com/cb");
        token.AccessToken.ShouldStartWith("oat_");
        token.Scope.ShouldBe("tasks:read");

        var oauthClient = BearerClient(token.AccessToken);
        var read = await oauthClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_token_scoped_only_for_read_is_rejected_for_a_write_action()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        // The application itself is allowed both scopes, but the authorize step only grants tasks:read —
        // exactly the "requested scope narrower than the app's ceiling" path.
        var app = await CreateAppAsync(client, new[] { "tasks:read", "tasks:write" });
        var code = await AuthorizeAsync(client, app, new[] { "tasks:read" });
        var token = await ExchangeCodeAsync(app, code, redirectUri: "https://example.com/cb");
        token.Scope.ShouldBe("tasks:read");

        var oauthClient = BearerClient(token.AccessToken);
        var create = await oauthClient.PostAsJsonAsync("/api/v1/tasks", new { listId = list.Id, title = "Should be rejected" });
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_application_cannot_be_granted_a_scope_it_was_never_allowed()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        // The application is only allowed tasks:read; asking for tasks:write at /oauth/authorize must not
        // silently grant it (OAuthApplication.FilterScopes' hard ceiling).
        var app = await CreateAppAsync(client, new[] { "tasks:read" });
        var code = await AuthorizeAsync(client, app, new[] { "tasks:read", "tasks:write" });
        var token = await ExchangeCodeAsync(app, code, redirectUri: "https://example.com/cb");

        token.Scope.ShouldBe("tasks:read");
    }

    [Fact]
    public async Task An_endpoint_with_no_oauth_scope_metadata_is_unreachable_via_an_oauth_token()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var app = await CreateAppAsync(client, new[] { "tasks:read", "workspace:read" });
        var code = await AuthorizeAsync(client, app, new[] { "tasks:read", "workspace:read" });
        var token = await ExchangeCodeAsync(app, code, redirectUri: "https://example.com/cb");

        // /api/v1/workspaces/me carries no RequiresOAuthScopeMetadata at all — default-deny applies even
        // though the token happens to carry workspace:read.
        var oauthClient = BearerClient(token.AccessToken);
        var response = await oauthClient.GetAsync(new Uri("/api/v1/workspaces/me", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_authorization_code_can_only_be_redeemed_once()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var app = await CreateAppAsync(client, new[] { "tasks:read" });
        var code = await AuthorizeAsync(client, app, new[] { "tasks:read" });

        await ExchangeCodeAsync(app, code, redirectUri: "https://example.com/cb");

        var anon = fixture.Factory.CreateClient();
        var replay = await anon.PostAsJsonAsync("/oauth/token", new
        {
            grantType = "authorization_code",
            clientId = app.ClientId,
            clientSecret = app.ClientSecret,
            code,
            redirectUri = "https://example.com/cb",
        });
        replay.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Oauth_applications_and_tokens_are_workspace_isolated()
    {
        var (clientA, workspaceAId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (clientB, workspaceBId, _, _) = await fixture.NewWorkspaceClientAsync();

        var appA = await CreateAppAsync(clientA, new[] { "tasks:read" });

        // Workspace B cannot see workspace A's application.
        var listInB = await clientB.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/v1/oauth-applications");
        listInB!.Any(e => e.GetProperty("id").GetGuid() == appA.Id).ShouldBeFalse();

        var codeA = await AuthorizeAsync(clientA, appA, new[] { "tasks:read" });
        var tokenA = await ExchangeCodeAsync(appA, codeA, redirectUri: "https://example.com/cb");

        // A token minted for workspace A, presented with an X-Workspace header for workspace B, must be
        // rejected — the token proves workspace A, and a mismatched header is a hard 403 (same rule PAT
        // authentication already enforces), never silently rebound to workspace B.
        var crossWorkspaceClient = BearerClient(tokenA.AccessToken);
        crossWorkspaceClient.DefaultRequestHeaders.Remove("X-Workspace");
        crossWorkspaceClient.DefaultRequestHeaders.Add("X-Workspace", workspaceBId.ToString());
        var response = await crossWorkspaceClient.GetAsync(new Uri("/api/v1/lists/00000000-0000-0000-0000-000000000000/tasks", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        _ = workspaceAId; // kept for readability of intent above
    }

    [Fact]
    public async Task An_application_cannot_authorize_with_an_unregistered_redirect_uri()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var app = await CreateAppAsync(client, new[] { "tasks:read" });

        var response = await client.PostAsJsonAsync("/oauth/authorize", new
        {
            clientId = app.ClientId,
            redirectUri = "https://not-registered.example.com/cb",
            scope = new[] { "tasks:read" },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- helpers ----

    private static async Task<CreatedOAuthApplicationResp> CreateAppAsync(HttpClient ownerClient, IReadOnlyList<string> allowedScopes)
    {
        var response = await ownerClient.PostAsJsonAsync("/api/v1/oauth-applications", new
        {
            name = "Test Integration",
            redirectUris = new[] { "https://example.com/cb" },
            allowedScopes,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedOAuthApplicationResp>())!;
    }

    private static async Task<string> AuthorizeAsync(HttpClient ownerClient, CreatedOAuthApplicationResp app, IReadOnlyList<string> scope)
    {
        var response = await ownerClient.PostAsJsonAsync("/oauth/authorize", new
        {
            clientId = app.ClientId,
            redirectUri = "https://example.com/cb",
            scope,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<OAuthAuthorizeResp>())!;
        return body.Code;
    }

    private async Task<OAuthTokenResp> ExchangeCodeAsync(CreatedOAuthApplicationResp app, string code, string redirectUri)
    {
        // The token endpoint is called by the third-party app's own backend — no Planvexa user session.
        var anon = fixture.Factory.CreateClient();
        var response = await anon.PostAsJsonAsync("/oauth/token", new
        {
            grantType = "authorization_code",
            clientId = app.ClientId,
            clientSecret = app.ClientSecret,
            code,
            redirectUri,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OAuthTokenResp>())!;
    }

    private HttpClient BearerClient(string accessToken)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
