namespace Planvexa.UnitTests.Governance;

using Planvexa.Modules.Governance.Domain;
using Planvexa.SharedContracts.Workspaces;
using Planvexa.Modules.Governance.Authorization;
using Shouldly;
using Xunit;

public sealed class EnterpriseSecuritySettingsTests
{
    private static EnterpriseSecuritySettings New()
        => EnterpriseSecuritySettings.CreateDefault(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    [Fact]
    public void Default_has_all_flags_off()
    {
        var s = New();
        s.SsoEnabled.ShouldBeFalse();
        s.ScimEnabled.ShouldBeFalse();
        s.MfaRequired.ShouldBeFalse();
        s.ScimTokenSet.ShouldBeFalse();
    }

    [Fact]
    public void Enabling_sso_requires_entity_id_and_metadata_url()
    {
        var s = New();
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            s.Update(ssoEnabled: true, samlEntityId: null, samlMetadataUrl: null, scimEnabled: null, mfaRequired: null, DateTimeOffset.UtcNow));

        // With valid config it succeeds.
        s.Update(true, "urn:planvexa:acme", "https://idp.example.com/metadata", null, null, DateTimeOffset.UtcNow);
        s.SsoEnabled.ShouldBeTrue();
        s.SamlEntityId.ShouldBe("urn:planvexa:acme");
    }

    [Fact]
    public void Invalid_metadata_url_is_rejected()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            New().Update(true, "urn:x", "not-a-url", null, null, DateTimeOffset.UtcNow));

    [Fact]
    public void Scim_token_is_stored_hashed_and_never_plaintext()
    {
        var s = New();
        s.SetScimToken("scim-secret-123", DateTimeOffset.UtcNow);
        s.ScimTokenSet.ShouldBeTrue();
        s.ScimTokenHash.ShouldNotBeNull();
        s.ScimTokenHash!.ShouldNotContain("scim-secret-123");

        s.ClearScimToken();
        s.ScimTokenSet.ShouldBeFalse();
    }
}

public sealed class ExportJobStateMachineTests
{
    private static ExportJob New(string dataset = "audit")
        => ExportJob.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), dataset, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    [Fact]
    public void Create_rejects_unknown_dataset()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() => New("bogus"));

    [Fact]
    public void Create_accepts_the_full_workspace_archive_dataset()
        => New("full").Dataset.ShouldBe("full");

    [Fact]
    public void Pending_to_running_to_completed()
    {
        var job = New("tasks");
        job.Status.ShouldBe(ExportJobStatus.Pending);
        job.Start(DateTimeOffset.UtcNow);
        job.Status.ShouldBe(ExportJobStatus.Running);
        job.Complete("id,title\r\n1,x", 1, DateTimeOffset.UtcNow);
        job.Status.ShouldBe(ExportJobStatus.Completed);
        job.RowCount.ShouldBe(1);
        job.Artifact.ShouldNotBeNull();
    }

    [Fact]
    public void Cannot_complete_without_running()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() => New().Complete("x", 0, DateTimeOffset.UtcNow));

    [Fact]
    public void Fail_records_error()
    {
        var job = New();
        job.Start(DateTimeOffset.UtcNow);
        job.Fail("boom", DateTimeOffset.UtcNow);
        job.Status.ShouldBe(ExportJobStatus.Failed);
        job.Error.ShouldBe("boom");
    }
}

public sealed class GovernanceAuthorizerTests
{
    [Theory]
    [InlineData(WorkspaceRole.Guest, false)]
    [InlineData(WorkspaceRole.Member, false)]
    [InlineData(WorkspaceRole.Admin, true)]
    [InlineData(WorkspaceRole.Owner, true)]
    public void Manage_requires_admin(WorkspaceRole role, bool allowed)
        => GovernanceAuthorizer.CanManage(role).ShouldBe(allowed);
}

public sealed class WorkspaceIpAllowRuleTests
{
    [Theory]
    [InlineData("203.0.113.0/24", "203.0.113.42", true)]
    [InlineData("203.0.113.0/24", "203.0.114.1", false)]
    [InlineData("203.0.113.10/32", "203.0.113.10", true)]
    [InlineData("203.0.113.10/32", "203.0.113.11", false)]
    [InlineData("2001:db8::/32", "2001:db8:1234::5", true)]
    [InlineData("2001:db8::/32", "2001:db9::5", false)]
    [InlineData("0.0.0.0/0", "8.8.8.8", true)]
    public void Matches_evaluates_cidr_membership(string cidr, string candidate, bool expected)
    {
        var rule = WorkspaceIpAllowRule.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), cidr, null, DateTimeOffset.UtcNow);
        rule.Matches(System.Net.IPAddress.Parse(candidate)).ShouldBe(expected);
    }

    [Fact]
    public void Matches_unwraps_ipv4_mapped_ipv6_addresses()
    {
        var rule = WorkspaceIpAllowRule.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "203.0.113.0/24", null, DateTimeOffset.UtcNow);
        rule.Matches(System.Net.IPAddress.Parse("::ffff:203.0.113.42")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("not-a-cidr")]
    [InlineData("203.0.113.0/33")]
    [InlineData("")]
    public void Create_rejects_invalid_cidr(string cidr)
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ValidationAppException>(() =>
            WorkspaceIpAllowRule.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), cidr, null, DateTimeOffset.UtcNow));
}
