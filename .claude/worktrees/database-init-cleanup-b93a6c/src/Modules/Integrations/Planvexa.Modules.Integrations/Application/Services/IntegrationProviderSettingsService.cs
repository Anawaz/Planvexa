namespace Planvexa.Modules.Integrations.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Integrations.Application;
using Planvexa.Modules.Integrations.Authorization;
using Planvexa.Modules.Integrations.Domain;

/// <summary>
/// Reads, updates and lists a workspace's third-party integration provider settings (Admin+). Follows
/// <c>AiSettingsService</c> exactly: the secret is write-only (reads return a mask; an update only
/// replaces the stored secret when a non-empty one is supplied).
/// </summary>
public sealed class IntegrationProviderSettingsService(
    IntegrationsServiceContext ctx,
    IIntegrationProviderSettingsStore store,
    IIntegrationSecretProtector protector)
    : IntegrationsServiceBase(ctx)
{
    public async Task<IReadOnlyList<IntegrationProviderSettingsDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageProviderSettings((await AccessAsync(workspaceId, ct))?.Role);

        var configured = (await store.ListByWorkspaceAsync(workspaceId, ct)).ToDictionary(s => s.Provider);
        return IntegrationProviders.All
            .Select(provider => configured.TryGetValue(provider, out var settings) ? ToDto(settings) : ToDefaultDto(provider))
            .ToList();
    }

    public async Task<IntegrationProviderSettingsDto> GetAsync(string provider, CancellationToken ct)
        => ToDto((await LoadAsync(provider, ct)).Settings);

    public async Task<IntegrationProviderSettingsDto> UpdateAsync(string provider, UpdateIntegrationProviderSettingsCommand command, CancellationToken ct)
    {
        var (settings, exists) = await LoadAsync(provider, ct);
        if (!exists)
        {
            store.Add(settings);
        }

        var newSecret = command.Secret?.Trim();
        settings.Update(
            command.ConfigJson,
            secretEncrypted: string.IsNullOrEmpty(newSecret) ? null : protector.Protect(newSecret),
            command.IsEnabled,
            Now);

        Audit("integrations.provider_settings.updated", "IntegrationProviderSettings", settings.Id,
            new { settings.Provider, settings.IsEnabled, SecretChanged = !string.IsNullOrEmpty(newSecret) });
        await SaveAsync(ct);
        return ToDto(settings);
    }

    private async Task<(IntegrationProviderSettings Settings, bool Exists)> LoadAsync(string provider, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        IntegrationsAuthorizer.EnsureManageProviderSettings((await AccessAsync(workspaceId, ct))?.Role);

        if (!IntegrationProviders.All.Contains(provider))
        {
            throw new NotFoundException($"Unknown integration provider '{provider}'.");
        }

        var settings = await store.FindAsync(workspaceId, provider, ct);
        return settings is null
            ? (IntegrationProviderSettings.CreateDefault(NewId(), workspaceId, provider, Now), false)
            : (settings, true);
    }

    private IntegrationProviderSettingsDto ToDto(IntegrationProviderSettings s)
        => new(s.Provider, s.ConfigJson, MaskSecret(protector.Unprotect(s.SecretEncrypted)), s.IsEnabled, IntegrationProviders.RealImplementation.Contains(s.Provider));

    private static IntegrationProviderSettingsDto ToDefaultDto(string provider)
        => new(provider, "{}", string.Empty, false, IntegrationProviders.RealImplementation.Contains(provider));

    private static string MaskSecret(string? secret)
        => string.IsNullOrEmpty(secret) ? string.Empty
            : secret.Length <= 4 ? new string('•', 3)
            : new string('•', 3) + secret[^4..];
}
