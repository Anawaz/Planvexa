namespace Planvexa.Modules.Governance.Application.Services;

using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Authorization;
using Planvexa.Modules.Governance.Domain;

/// <summary>Manages workspace-wide enterprise security settings.</summary>
public sealed class SecuritySettingsService(
    GovernanceServiceContext ctx,
    ISecuritySettingsStore store)
    : GovernanceServiceBase(ctx)
{
    public async Task<SecuritySettingsDto> GetAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var settings = await store.FindAsync(workspaceId, ct)
            ?? EnterpriseSecuritySettings.CreateDefault(NewId(), workspaceId, Now);
        return ToDto(settings);
    }

    public async Task<SecuritySettingsDto> UpdateAsync(UpdateSecuritySettingsCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var settings = await store.FindAsync(workspaceId, ct);
        if (settings is null)
        {
            settings = EnterpriseSecuritySettings.CreateDefault(NewId(), workspaceId, Now);
            store.Add(settings);
        }

        var previous = new { settings.SsoEnabled, settings.ScimEnabled, settings.MfaRequired };
        settings.Update(command.SsoEnabled, command.SamlEntityId, command.SamlMetadataUrl, command.ScimEnabled, command.MfaRequired, Now);
        if (!string.IsNullOrWhiteSpace(command.ScimToken))
        {
            settings.SetScimToken(command.ScimToken, Now);
        }

        if (command.ScimEnabled == false)
        {
            settings.ClearScimToken();
        }

        var next = new { settings.SsoEnabled, settings.ScimEnabled, settings.MfaRequired };
        Audit("governance.security_settings.updated", "EnterpriseSecuritySettings", settings.Id, new { previous, next });
        await SaveAsync(ct);
        return ToDto(settings);
    }

    private static SecuritySettingsDto ToDto(EnterpriseSecuritySettings settings)
        => new(settings.SsoEnabled, settings.SamlEntityId, settings.SamlMetadataUrl, settings.ScimEnabled, settings.ScimTokenSet, settings.MfaRequired);
}

