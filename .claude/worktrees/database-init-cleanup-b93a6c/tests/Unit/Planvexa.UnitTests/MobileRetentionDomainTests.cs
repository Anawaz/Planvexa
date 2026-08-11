namespace Planvexa.UnitTests.Platform;

using Planvexa.Modules.Mobile.Domain;
using Planvexa.Modules.Governance.Domain;
using Shouldly;
using Xunit;

public sealed class DeviceRegistrationTests
{
    [Fact]
    public void Register_hashes_the_push_token()
    {
        var device = DeviceRegistration.Register(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DevicePlatform.Ios, "raw-apns-token", "1.2.0", DateTimeOffset.UtcNow);
        device.TokenHash.ShouldBe(DeviceRegistration.HashToken("raw-apns-token"));
        device.TokenHash.ShouldNotContain("raw-apns-token");
        device.AppVersion.ShouldBe("1.2.0");
    }

    [Fact]
    public void HashToken_is_deterministic_and_distinct()
    {
        DeviceRegistration.HashToken("a").ShouldBe(DeviceRegistration.HashToken("a"));
        DeviceRegistration.HashToken("a").ShouldNotBe(DeviceRegistration.HashToken("b"));
    }

    [Fact]
    public void Register_stores_the_raw_push_subscription_fields_when_supplied()
    {
        var device = DeviceRegistration.Register(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DevicePlatform.Web, "raw-web-push-token",
            null, DateTimeOffset.UtcNow,
            pushEndpoint: "https://push.example.com/subscription/abc", pushP256dh: "p256dh-key", pushAuth: "auth-secret");

        device.PushEndpoint.ShouldBe("https://push.example.com/subscription/abc");
        device.PushP256dh.ShouldBe("p256dh-key");
        device.PushAuth.ShouldBe("auth-secret");
    }

    [Fact]
    public void Register_leaves_push_subscription_fields_null_when_not_supplied()
    {
        var device = DeviceRegistration.Register(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DevicePlatform.Ios, "raw-apns-token", null, DateTimeOffset.UtcNow);

        device.PushEndpoint.ShouldBeNull();
        device.PushP256dh.ShouldBeNull();
        device.PushAuth.ShouldBeNull();
    }

    [Fact]
    public void Touch_advances_last_seen()
    {
        var device = DeviceRegistration.Register(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), DevicePlatform.Android, "t", null, DateTimeOffset.Parse("2026-03-01Z"));
        device.Touch(DateTimeOffset.Parse("2026-03-05Z"));
        device.LastSeenAtUtc.ShouldBe(DateTimeOffset.Parse("2026-03-05Z"));
    }
}

public sealed class RetentionPolicyTests
{
    private static RetentionPolicy New()
        => RetentionPolicy.CreateDefault(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    [Fact]
    public void Default_keeps_forever_and_no_hold()
    {
        var p = New();
        p.DeletedTaskRetentionDays.ShouldBe(0);
        p.LegalHold.ShouldBeFalse();
        p.PurgeCutoff(DateTimeOffset.UtcNow).ShouldBeNull(); // keep forever
    }

    [Fact]
    public void Cutoff_is_now_minus_window_when_enabled()
    {
        var p = New();
        var now = DateTimeOffset.Parse("2026-03-31T00:00:00Z");
        p.Update(deletedTaskRetentionDays: 30, auditRetentionDays: null, legalHold: null, now);
        p.PurgeCutoff(now).ShouldBe(now.AddDays(-30));
    }

    [Fact]
    public void Legal_hold_disables_purging()
    {
        var p = New();
        var now = DateTimeOffset.Parse("2026-03-31T00:00:00Z");
        p.Update(deletedTaskRetentionDays: 30, auditRetentionDays: null, legalHold: true, now);
        p.PurgeCutoff(now).ShouldBeNull(); // legal hold blocks purge even with a window
    }

    [Fact]
    public void Negative_retention_is_rejected()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            New().Update(deletedTaskRetentionDays: -1, auditRetentionDays: null, legalHold: null, DateTimeOffset.UtcNow));
}

