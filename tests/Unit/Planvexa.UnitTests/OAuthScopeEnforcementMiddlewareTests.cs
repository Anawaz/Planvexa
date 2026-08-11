namespace Planvexa.UnitTests.Integrations;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Planvexa.Api.Middleware;
using Shouldly;
using Xunit;

/// <summary>
/// The scope-enforcement half of the OAuth/PAT privilege boundary: default-deny for an OAuth-token- or
/// PAT-authenticated request unless the matched endpoint opted in with a required scope the token was
/// actually granted (a PAT with zero granted scopes is the backward-compatible exception — see below).
/// Fully offline — no HTTP host, just <see cref="DefaultHttpContext"/> with a hand-built
/// endpoint/principal, exactly like the middleware sees them once routing has matched.
/// </summary>
public sealed class OAuthScopeEnforcementMiddlewareTests
{
    [Fact]
    public async Task Allows_an_oauth_request_when_the_endpoint_requires_a_scope_the_token_has()
    {
        var context = BuildContext(scopes: ["tasks:read"], requiredScope: "tasks:read");
        var nextCalled = false;
        var middleware = new OAuthScopeEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Denies_an_oauth_request_when_the_token_lacks_the_endpoints_required_scope()
    {
        var context = BuildContext(scopes: ["tasks:read"], requiredScope: "tasks:write");
        var nextCalled = false;
        var middleware = new OAuthScopeEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Denies_an_oauth_request_to_an_endpoint_with_no_scope_metadata_at_all()
    {
        // Default-deny: an endpoint that never opted into OAuth access (no RequiresOAuthScopeMetadata) is
        // unreachable via an OAuth token, even if the token happens to carry some scope.
        var context = BuildContext(scopes: ["tasks:read", "tasks:write"], requiredScope: null);
        var nextCalled = false;
        var middleware = new OAuthScopeEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Does_not_touch_a_non_oauth_non_pat_authenticated_request()
    {
        // A normal JWT/dev-auth session has neither "OAuth" nor "Pat" AuthenticationType, so this
        // middleware must be a pure no-op for it regardless of endpoint metadata.
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Bearer"));
        var nextCalled = false;
        var middleware = new OAuthScopeEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_pat_with_no_granted_scopes_is_full_access_for_backward_compatibility()
    {
        // Tokens created before scope enforcement existed (or with no recognized scopes) carry zero
        // "scope" claims. Treat that as a legacy, unrestricted token rather than default-deny — otherwise
        // every pre-existing PAT would suddenly be locked out of every endpoint.
        var context = BuildContext(scopes: [], requiredScope: null, scheme: PatAuthenticationMiddleware.Scheme);
        var nextCalled = false;
        var middleware = new OAuthScopeEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_scoped_pat_is_denied_outside_its_granted_scope()
    {
        var context = BuildContext(scopes: ["tasks:read"], requiredScope: "tasks:write", scheme: PatAuthenticationMiddleware.Scheme);
        var nextCalled = false;
        var middleware = new OAuthScopeEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeFalse();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task A_scoped_pat_is_allowed_within_its_granted_scope()
    {
        var context = BuildContext(scopes: ["tasks:read"], requiredScope: "tasks:read", scheme: PatAuthenticationMiddleware.Scheme);
        var nextCalled = false;
        var middleware = new OAuthScopeEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeTrue();
    }

    private static DefaultHttpContext BuildContext(IReadOnlyList<string> scopes, string? requiredScope, string? scheme = null)
    {
        var claims = scopes.Select(s => new Claim("scope", s)).ToList();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme ?? OAuthAuthenticationMiddleware.Scheme));

        var metadata = requiredScope is null
            ? new EndpointMetadataCollection()
            : new EndpointMetadataCollection(new RequiresOAuthScopeMetadata(requiredScope));
        var endpoint = new Endpoint(_ => Task.CompletedTask, metadata, "test");
        context.Features.Set<IEndpointFeature>(new EndpointFeatureStub(endpoint));

        return context;
    }

    private sealed class EndpointFeatureStub(Endpoint endpoint) : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; } = endpoint;
    }
}
