namespace Planvexa.UnitTests.Governance;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Application.Services;
using Planvexa.Modules.Governance.Domain;
using Planvexa.SharedContracts.Workspaces;
using Planvexa.UnitTests.Tenancy;
using Planvexa.UnitTests.Whiteboards;
using Shouldly;
using Xunit;

/// <summary>Security settings toggles (SSO/SCIM/MFA) must audit both the previous and new state.</summary>
public sealed class SecuritySettingsServiceAuditTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task UpdateAsync_audits_previous_and_next_settings()
    {
        var existing = EnterpriseSecuritySettings.CreateDefault(Guid.NewGuid(), WorkspaceId, DateTimeOffset.UtcNow);
        // Start from a non-default state so "previous" is verifiably distinct from "next".
        existing.Update(ssoEnabled: true, samlEntityId: "urn:entity", samlMetadataUrl: "https://idp.example/meta", scimEnabled: false, mfaRequired: false, DateTimeOffset.UtcNow);

        var store = new FakeSecuritySettingsStore(existing);
        var audit = new CapturingAuditWriter();
        var accessor = new WorkspaceContextAccessor();
        accessor.Set(new WorkspaceContext(
            WorkspaceId, UserId, null, "Admin", new HashSet<string>(), new HashSet<string>(), "corr"));

        var ctx = new GovernanceServiceContext(
            accessor,
            new FakeCurrentUser(UserId),
            new FakeIdGenerator(),
            new FakeClock(),
            audit,
            new FakeWorkspaceAccessQuery(WorkspaceRole.Admin),
            new FakeUnitOfWork());

        var svc = new SecuritySettingsService(ctx, store);

        await svc.UpdateAsync(
            new UpdateSecuritySettingsCommand(SsoEnabled: false, SamlEntityId: null, SamlMetadataUrl: null, ScimEnabled: true, ScimToken: null, MfaRequired: true),
            CancellationToken.None);

        var entry = audit.Entries.Single(e => e.Action == "governance.security_settings.updated");
        var previous = entry.Data!.GetType().GetProperty("previous")!.GetValue(entry.Data)!;
        var next = entry.Data!.GetType().GetProperty("next")!.GetValue(entry.Data)!;

        GetBool(previous, "SsoEnabled").ShouldBeTrue();
        GetBool(previous, "ScimEnabled").ShouldBeFalse();
        GetBool(previous, "MfaRequired").ShouldBeFalse();

        GetBool(next, "SsoEnabled").ShouldBeFalse();
        GetBool(next, "ScimEnabled").ShouldBeTrue();
        GetBool(next, "MfaRequired").ShouldBeTrue();
    }

    private static bool GetBool(object obj, string propertyName) => (bool)obj.GetType().GetProperty(propertyName)!.GetValue(obj)!;

    private sealed class FakeSecuritySettingsStore(EnterpriseSecuritySettings settings) : ISecuritySettingsStore
    {
        public void Add(EnterpriseSecuritySettings s) => throw new NotSupportedException("Test seeds settings directly.");
        public Task<EnterpriseSecuritySettings?> FindAsync(Guid workspaceId, CancellationToken ct = default)
            => Task.FromResult<EnterpriseSecuritySettings?>(workspaceId == settings.WorkspaceId ? settings : null);
    }
}
