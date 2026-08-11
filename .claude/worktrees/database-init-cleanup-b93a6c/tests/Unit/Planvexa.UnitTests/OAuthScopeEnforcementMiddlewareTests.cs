namespace Planvexa.UnitTests.Integrations;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Planvexa.Api.Middleware;
using Shouldly;
using Xunit;

/// <summary>
/// The scope-enforcement half of the OAuth privilege boundary (): default-deny for an
/// OAuth-token-authenticated request unless the matched endpoint opted in with a required scope the
/// token was actually granted. Fully offline — no HTTP host, just <see cref="DefaultHttpContext"/> with a
/// hand-built endpoint/principal, exactly like the middleware sees them once routing has matched.
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
    public async Task Does_not_touch_a_non_oauth_authenticated_request()
    {
        // A normal JWT/dev-auth/PAT session has no "OAuth" AuthenticationType, so this middleware must be
        // a pure no-op for it regardless of endpoint metadata (PATs keep their existing unscoped behavior).
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Bearer"));
        var nextCalled = false;
        var middleware = new OAuthScopeEnforcementMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.ShouldBeTrue();
    }

    private static DefaultHttpContext BuildContext(IReadOnlyList<string> scopes, string? requiredScope)
    {
        var claims = scopes.Select(s => new Claim("scope", s)).ToList();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, OAuthAuthenticationMiddleware.Scheme));

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
