namespace Planvexa.UnitTests.HostAdministration;

using Microsoft.Extensions.Configuration;
using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.Modules.Identity.Domain;
using Shouldly;
using Xunit;

/// <summary>
/// The host-administration rules that are decided in pure code: the domain flag transitions, the
/// break-glass configuration parser, and the log-level ladder. Everything that depends on RLS or the
/// HTTP pipeline is covered by HostAdminFlowTests against a real database instead.
/// </summary>
public sealed class HostAdminUserTests
{
    private static User NewUser() =>
        User.Provision(Guid.CreateVersion7(), "sub-1", "person@example.test", "Person", DateTimeOffset.UtcNow);

    [Fact]
    public void A_new_user_is_active_and_not_a_host_admin()
    {
        var user = NewUser();

        user.IsActive.ShouldBeTrue();
        user.IsHostAdmin.ShouldBeFalse();
    }

    [Fact]
    public void Granting_and_revoking_host_admin_is_idempotent_and_stamps_only_on_change()
    {
        var user = NewUser();
        var now = DateTimeOffset.UtcNow;

        user.GrantHostAdmin(now);
        user.IsHostAdmin.ShouldBeTrue();
        user.UpdatedAtUtc.ShouldBe(now);

        // Second grant is a no-op: it must not bump UpdatedAtUtc, which would make an unchanged row
        // look freshly edited in the console.
        user.GrantHostAdmin(now.AddHours(1));
        user.UpdatedAtUtc.ShouldBe(now);

        user.RevokeHostAdmin(now.AddHours(2));
        user.IsHostAdmin.ShouldBeFalse();
        user.UpdatedAtUtc.ShouldBe(now.AddHours(2));
    }

    [Fact]
    public void Deactivate_and_reactivate_flip_IsActive()
    {
        var user = NewUser();
        var now = DateTimeOffset.UtcNow;

        user.Deactivate(now);
        user.IsActive.ShouldBeFalse();

        user.Reactivate(now.AddMinutes(1));
        user.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void An_anonymized_account_loses_host_admin_and_cannot_be_reactivated()
    {
        var user = NewUser();
        var now = DateTimeOffset.UtcNow;
        user.GrantHostAdmin(now);

        user.Anonymize(now.AddDays(1));

        // Left flagged, a deleted account would keep counting toward the last-host-admin guard and so
        // permanently block the real host admin from being demoted.
        user.IsHostAdmin.ShouldBeFalse();
        user.IsActive.ShouldBeFalse();

        // Its PII is gone, so "reactivating" it could only produce a login-less shell.
        Should.Throw<InvalidOperationException>(() => user.Reactivate(now.AddDays(2)));
    }
}

public sealed class HostAdminBreakGlassTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void An_empty_configuration_grants_nobody()
    {
        HostAdminAuthorizationHandler.IsBreakGlassSubject(Config(), "sub-1").ShouldBeFalse();
    }

    [Fact]
    public void An_indexed_array_is_matched()
    {
        var configuration = Config(
            ("HostAdmin:Subjects:0", "sub-rescue"),
            ("HostAdmin:Subjects:1", "sub-other"));

        HostAdminAuthorizationHandler.IsBreakGlassSubject(configuration, "sub-other").ShouldBeTrue();
        HostAdminAuthorizationHandler.IsBreakGlassSubject(configuration, "sub-nope").ShouldBeFalse();
    }

    [Fact]
    public void A_comma_separated_value_is_matched()
    {
        // What HostAdmin__Subjects=a,b in a container environment actually produces.
        var configuration = Config(("HostAdmin:Subjects", "sub-a, sub-b"));

        HostAdminAuthorizationHandler.IsBreakGlassSubject(configuration, "sub-a").ShouldBeTrue();
        HostAdminAuthorizationHandler.IsBreakGlassSubject(configuration, "sub-b").ShouldBeTrue();
        HostAdminAuthorizationHandler.IsBreakGlassSubject(configuration, "sub-c").ShouldBeFalse();
    }

    [Fact]
    public void A_blank_subject_never_matches()
    {
        // An unauthenticated caller has no subject; a blank entry in the list must not become a wildcard.
        var configuration = Config(("HostAdmin:Subjects", "sub-a,,sub-b"));

        HostAdminAuthorizationHandler.IsBreakGlassSubject(configuration, string.Empty).ShouldBeFalse();
        HostAdminAuthorizationHandler.IsBreakGlassSubject(configuration, "   ").ShouldBeFalse();
    }

    [Fact]
    public void Matching_is_case_sensitive()
    {
        // Identity-provider subjects are opaque, case-sensitive identifiers; loosening this would let a
        // different subject that merely differs in case reach the console.
        var configuration = Config(("HostAdmin:Subjects", "sub-Rescue"));

        HostAdminAuthorizationHandler.IsBreakGlassSubject(configuration, "sub-rescue").ShouldBeFalse();
        HostAdminAuthorizationHandler.IsBreakGlassSubject(configuration, "sub-Rescue").ShouldBeTrue();
    }
}

public sealed class InstanceSettingsTests
{
    [Fact]
    public void A_null_field_leaves_the_current_value_alone()
    {
        var settings = InstanceSettings.CreateDefault(allowSelfRegistration: true);
        var now = DateTimeOffset.UtcNow;
        settings.Update(null, null, "Acme", null, "help@acme.test", Guid.NewGuid(), now);

        // Branding form submitted; the access form's fields must be untouched.
        settings.Update(null, null, null, null, null, Guid.NewGuid(), now);

        settings.InstanceName.ShouldBe("Acme");
        settings.SupportEmail.ShouldBe("help@acme.test");
        settings.AllowSelfRegistration.ShouldBeTrue();
    }

    [Fact]
    public void A_blank_string_clears_the_value_rather_than_storing_an_empty_one()
    {
        var settings = InstanceSettings.CreateDefault(allowSelfRegistration: true);
        var now = DateTimeOffset.UtcNow;
        settings.Update(null, null, "Acme", "https://acme.test/logo.png", "help@acme.test", null, now);

        settings.Update(null, null, "  ", "", "   ", null, now);

        settings.InstanceName.ShouldBeNull();
        settings.LogoUrl.ShouldBeNull();
        settings.SupportEmail.ShouldBeNull();
    }

    [Fact]
    public void An_unrecognised_workspace_creation_policy_falls_back_to_the_permissive_one()
    {
        // Defence for a hand-edited database row: an unknown value must never silently lock workspace
        // creation for the whole installation. Request validation rejects it long before this.
        WorkspaceCreationPolicies.Normalize("nonsense").ShouldBe(WorkspaceCreationPolicies.Anyone);
        WorkspaceCreationPolicies.Normalize(null).ShouldBe(WorkspaceCreationPolicies.Anyone);
        WorkspaceCreationPolicies.Normalize("hostadminsonly").ShouldBe(WorkspaceCreationPolicies.HostAdminsOnly);
    }
}
