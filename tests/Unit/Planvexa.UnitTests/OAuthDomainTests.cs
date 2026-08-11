namespace Planvexa.UnitTests.Integrations;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Integrations.Domain;
using Shouldly;
using Xunit;

public sealed class OAuthApplicationTests
{
    private static readonly Guid WorkspaceId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void Create_returns_prefixed_client_id_and_stores_only_secret_hash()
    {
        var (app, rawSecret) = OAuthApplication.Create(
            Guid.CreateVersion7(), WorkspaceId, "My App", new[] { "https://example.com/callback" },
            new[] { OAuthScopes.TasksRead }, UserId, DateTimeOffset.UtcNow);

        app.ClientId.ShouldStartWith(OAuthApplication.ClientIdPrefix);
        app.ClientSecretHash.ShouldNotContain(rawSecret);
        app.VerifySecret(rawSecret).ShouldBeTrue();
        app.VerifySecret("wrong").ShouldBeFalse();
    }

    [Fact]
    public void Create_rejects_a_non_absolute_http_redirect_uri()
        => Should.Throw<ValidationAppException>(() => OAuthApplication.Create(
            Guid.CreateVersion7(), WorkspaceId, "App", new[] { "not-a-uri" }, new[] { OAuthScopes.TasksRead }, UserId, DateTimeOffset.UtcNow));

    [Fact]
    public void Create_drops_unknown_scopes_and_rejects_when_none_remain()
    {
        var (app, _) = OAuthApplication.Create(
            Guid.CreateVersion7(), WorkspaceId, "App", new[] { "https://example.com/cb" },
            new[] { OAuthScopes.TasksRead, "bogus:scope" }, UserId, DateTimeOffset.UtcNow);
        app.AllowedScopes.ShouldBe(new[] { OAuthScopes.TasksRead });

        Should.Throw<ValidationAppException>(() => OAuthApplication.Create(
            Guid.CreateVersion7(), WorkspaceId, "App", new[] { "https://example.com/cb" }, new[] { "bogus:scope" }, UserId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FilterScopes_never_exceeds_the_applications_own_allowed_scopes()
    {
        var (app, _) = OAuthApplication.Create(
            Guid.CreateVersion7(), WorkspaceId, "App", new[] { "https://example.com/cb" },
            new[] { OAuthScopes.TasksRead, OAuthScopes.WorkspaceRead }, UserId, DateTimeOffset.UtcNow);

        // A caller asking for tasks:write (never granted to the app) must not get it back, even though
        // it's a valid scope in the global vocabulary — this is the hard ceiling the security brief calls
        // out: a scoped token can never exceed what its OWN application was allowed.
        var granted = app.FilterScopes(new[] { OAuthScopes.TasksRead, OAuthScopes.TasksWrite, OAuthScopes.DocsRead });
        granted.ShouldBe(new[] { OAuthScopes.TasksRead });
    }

    [Fact]
    public void IsRedirectUriAllowed_is_exact_match_only()
    {
        var (app, _) = OAuthApplication.Create(
            Guid.CreateVersion7(), WorkspaceId, "App", new[] { "https://example.com/cb" }, new[] { OAuthScopes.TasksRead }, UserId, DateTimeOffset.UtcNow);

        app.IsRedirectUriAllowed("https://example.com/cb").ShouldBeTrue();
        app.IsRedirectUriAllowed("https://example.com/cb/").ShouldBeFalse();
        app.IsRedirectUriAllowed("https://evil.example.com/cb").ShouldBeFalse();
    }
}

public sealed class OAuthAuthorizationCodeTests
{
    [Fact]
    public void A_code_is_single_use()
    {
        var now = DateTimeOffset.UtcNow;
        var (code, _) = OAuthAuthorizationCode.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "https://example.com/cb", new[] { OAuthScopes.TasksRead }, now);

        code.IsRedeemable(now).ShouldBeTrue();
        code.MarkUsed(now);
        code.IsRedeemable(now).ShouldBeFalse();
    }

    [Fact]
    public void A_code_expires()
    {
        var now = DateTimeOffset.UtcNow;
        var (code, _) = OAuthAuthorizationCode.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "https://example.com/cb", new[] { OAuthScopes.TasksRead }, now);

        code.IsRedeemable(now.AddMinutes(11)).ShouldBeFalse();
    }
}

public sealed class OAuthTokenTests
{
    [Fact]
    public void Create_returns_prefixed_raw_tokens_and_stores_only_hashes()
    {
        var (token, rawAccess, rawRefresh) = OAuthToken.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new[] { OAuthScopes.TasksRead, OAuthScopes.TasksWrite }, DateTimeOffset.UtcNow);

        rawAccess.ShouldStartWith(OAuthToken.AccessTokenPrefix);
        rawRefresh.ShouldStartWith(OAuthToken.RefreshTokenPrefix);
        token.AccessTokenHash.ShouldNotContain(rawAccess);
        token.HasScope(OAuthScopes.TasksRead).ShouldBeTrue();
        token.HasScope(OAuthScopes.WorkspaceRead).ShouldBeFalse();
    }

    [Fact]
    public void Access_token_expires_and_revocation_disables_it_immediately()
    {
        var now = DateTimeOffset.UtcNow;
        var (token, _, _) = OAuthToken.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new[] { OAuthScopes.TasksRead }, now);

        token.IsAccessTokenUsable(now).ShouldBeTrue();
        token.IsAccessTokenUsable(now.AddHours(2)).ShouldBeFalse();

        token.Revoke(now);
        token.IsAccessTokenUsable(now).ShouldBeFalse();
        token.IsRefreshTokenUsable(now).ShouldBeFalse();
    }

    [Fact]
    public void Rotate_issues_new_tokens_and_invalidates_the_old_pair()
    {
        var now = DateTimeOffset.UtcNow;
        var (token, rawAccess, rawRefresh) = OAuthToken.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), new[] { OAuthScopes.TasksRead }, now);
        var oldAccessHash = token.AccessTokenHash;
        var oldRefreshHash = token.RefreshTokenHash;

        var (newAccess, newRefresh) = token.Rotate(now);

        newAccess.ShouldNotBe(rawAccess);
        newRefresh.ShouldNotBe(rawRefresh);
        token.AccessTokenHash.ShouldNotBe(oldAccessHash);
        token.RefreshTokenHash.ShouldNotBe(oldRefreshHash);
    }
}
