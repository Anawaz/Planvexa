namespace Planvexa.UnitTests.Integrations;

using Planvexa.Modules.Integrations.Domain;
using Shouldly;
using Xunit;

public sealed class SecretCryptoTests
{
    [Fact]
    public void GenerateSecret_is_random_hex_and_unique()
    {
        var a = SecretCrypto.GenerateSecret();
        var b = SecretCrypto.GenerateSecret();
        a.ShouldNotBe(b);
        a.ShouldMatch("^[0-9a-f]+$");
        a.Length.ShouldBe(64); // 32 bytes hex
    }

    [Fact]
    public void Hash_is_deterministic_and_hides_input()
    {
        var raw = "pat_secret_value";
        SecretCrypto.Hash(raw).ShouldBe(SecretCrypto.Hash(raw));
        SecretCrypto.Hash(raw).ShouldNotContain("secret");
        SecretCrypto.Hash("a").ShouldNotBe(SecretCrypto.Hash("b"));
    }

    [Fact]
    public void Sign_is_stable_hmac_and_key_sensitive()
    {
        var payload = "{\"event\":\"task.created\"}";
        var sig1 = SecretCrypto.Sign("secret1", payload);
        var sig2 = SecretCrypto.Sign("secret1", payload);
        var sig3 = SecretCrypto.Sign("secret2", payload);
        sig1.ShouldBe(sig2);
        sig1.ShouldNotBe(sig3);
        sig1.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void SignWithTimestamp_produces_a_header_verify_accepts_within_tolerance()
    {
        var now = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var payload = "{\"event\":\"task.created\"}";
        var header = SecretCrypto.SignWithTimestamp("secret1", payload, now);

        header.ShouldMatch("^t=[0-9]+,v1=[0-9a-f]{64}$");
        SecretCrypto.VerifyTimestampedSignature("secret1", payload, header, toleranceSeconds: 300, now).ShouldBeTrue();
    }

    [Fact]
    public void VerifyTimestampedSignature_rejects_a_replayed_delivery_outside_the_tolerance_window()
    {
        var sentAt = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var payload = "{\"event\":\"task.created\"}";
        var header = SecretCrypto.SignWithTimestamp("secret1", payload, sentAt);

        // Signature itself is still valid; only the elapsed time makes it a replay.
        SecretCrypto.VerifyTimestampedSignature("secret1", payload, header, toleranceSeconds: 300, sentAt.AddSeconds(301)).ShouldBeFalse();
        SecretCrypto.VerifyTimestampedSignature("secret1", payload, header, toleranceSeconds: 300, sentAt.AddSeconds(299)).ShouldBeTrue();
    }

    [Fact]
    public void VerifyTimestampedSignature_rejects_wrong_secret_or_tampered_payload()
    {
        var now = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var payload = "{\"event\":\"task.created\"}";
        var header = SecretCrypto.SignWithTimestamp("secret1", payload, now);

        SecretCrypto.VerifyTimestampedSignature("wrong-secret", payload, header, 300, now).ShouldBeFalse();
        SecretCrypto.VerifyTimestampedSignature("secret1", "{\"event\":\"task.deleted\"}", header, 300, now).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("t=notanumber,v1=abc")]
    [InlineData("v1=abc")]
    public void VerifyTimestampedSignature_rejects_malformed_headers(string header)
    {
        var now = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        SecretCrypto.VerifyTimestampedSignature("secret1", "{}", header, 300, now).ShouldBeFalse();
    }
}

public sealed class PersonalAccessTokenTests
{
    [Fact]
    public void Create_returns_prefixed_raw_and_stores_only_hash()
    {
        var (token, raw) = PersonalAccessToken.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "sub", "e@x.io", "Ed",
            "ci", new[] { "read", "write" }, null, DateTimeOffset.UtcNow);

        raw.ShouldStartWith("pat_");
        token.TokenHash.ShouldBe(SecretCrypto.Hash(raw));
        token.TokenHash.ShouldNotContain(raw);
        token.Scopes.ShouldBe(new[] { "read", "write" });
    }

    [Fact]
    public void Expiry_governs_usability()
    {
        var now = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var (active, _) = PersonalAccessToken.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "s", "e", "n", "t", Array.Empty<string>(), now.AddDays(1), now);
        active.IsUsable(now).ShouldBeTrue();
        active.IsUsable(now.AddDays(2)).ShouldBeFalse();

        var (noExpiry, _) = PersonalAccessToken.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "s", "e", "n", "t", Array.Empty<string>(), null, now);
        noExpiry.IsUsable(now.AddYears(5)).ShouldBeTrue();
    }
}

public sealed class WebhookSubscriptionTests
{
    [Fact]
    public void Create_rejects_non_http_url()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            WebhookSubscription.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "ftp://x", new[] { "task.created" }, Guid.CreateVersion7(), DateTimeOffset.UtcNow));

    [Fact]
    public void Create_requires_a_valid_event_type()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            WebhookSubscription.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "https://x.io/hook", new[] { "bogus" }, Guid.CreateVersion7(), DateTimeOffset.UtcNow));

    [Fact]
    public void Subscription_tracks_event_types_and_generates_secret()
    {
        var sub = WebhookSubscription.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "https://x.io/hook", new[] { "task.created", "task.completed", "bogus" }, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        sub.IsSubscribedTo("task.created").ShouldBeTrue();
        sub.IsSubscribedTo("task.assigned").ShouldBeFalse();
        sub.Secret.ShouldNotBeNullOrWhiteSpace();
        sub.EventTypes.Count.ShouldBe(2); // bogus dropped
    }
}

