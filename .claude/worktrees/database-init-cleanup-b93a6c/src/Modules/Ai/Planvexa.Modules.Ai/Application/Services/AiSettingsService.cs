namespace Planvexa.Modules.Ai.Application.Services;

using Planvexa.Modules.Ai.Application;
using Planvexa.Modules.Ai.Authorization;
using Planvexa.Modules.Ai.Domain;
using Planvexa.SharedContracts.Ai;

/// <summary>
/// Reads, updates and probes a workspace's AI provider settings (Admin+). The API key is write-only: reads
/// return a mask, and an update only replaces the stored key when a non-empty one is supplied.
/// </summary>
public sealed class AiSettingsService(
    AiServiceContext ctx,
    IAiProviderSettingsStore store,
    IAiSecretProtector protector,
    IAiProviderProbe probe)
    : AiServiceBase(ctx)
{
    public async Task<AiProviderSettingsDto> GetAsync(CancellationToken ct)
        => ToDto((await LoadAsync(ct)).Settings);

    public async Task<AiProviderSettingsDto> UpdateAsync(UpdateAiProviderSettingsCommand command, CancellationToken ct)
    {
        var (settings, exists) = await LoadAsync(ct);
        if (!exists)
        {
            store.Add(settings);
        }

        var newKey = command.ApiKey?.Trim();
        settings.Update(
            command.BaseUrl,
            command.Model,
            apiKeyEncrypted: string.IsNullOrEmpty(newKey) ? null : protector.Protect(newKey),
            command.IsEnabled,
            Now);

        Audit("ai.provider_settings.updated", "AiProviderSettings", settings.Id,
            new { settings.BaseUrl, settings.Model, settings.IsEnabled, KeyChanged = !string.IsNullOrEmpty(newKey) });
        await SaveAsync(ct);
        return ToDto(settings);
    }

    /// <summary>
    /// Probes the endpoint with a minimal completion. Uses the supplied candidate values so a
    /// configuration can be verified before it is saved or enabled; a blank key falls back to the stored
    /// one, matching the write-only key semantics of <see cref="UpdateAsync" />.
    /// </summary>
    public async Task<AiProviderTestDto> TestAsync(UpdateAiProviderSettingsCommand candidate, CancellationToken ct)
    {
        var (settings, _) = await LoadAsync(ct);
        var baseUrl =(candidate.BaseUrl ?? string.Empty).Trim().TrimEnd('/') is { Length: > 0 } url ? url : settings.BaseUrl;
        var model = (candidate.Model ?? string.Empty).Trim() is { Length: > 0 } m ? m : settings.Model;
        var apiKey = candidate.ApiKey?.Trim() is { Length: > 0 } k ? k : protector.Unprotect(settings.ApiKeyEncrypted);

        if (baseUrl.Length == 0 || model.Length == 0)
        {
            return new AiProviderTestDto(false, "A base URL and model are required to test the connection.");
        }

        var error = await probe.TestAsync(baseUrl, model, apiKey, ct);
        return error is null
            ? new AiProviderTestDto(true, $"Connected to {baseUrl} using {model}.")
            : new AiProviderTestDto(false, error);
    }

    /// <summary>item 2+3: the workspace's model allow-list and redaction configuration (Admin+).</summary>
    public async Task<AiGovernanceDto> GetGovernanceAsync(CancellationToken ct)
        => ToGovernanceDto((await LoadAsync(ct)).Settings);

    /// <summary>
    /// Updates the model allow-list and redaction configuration (Admin+). Rejects an allow-list that would
    /// make the currently-configured model disallowed (see <see cref="AiProviderSettings.UpdateGovernance"/>) —
    /// tighten the allow-list and repoint the model in two separate requests, never in a way that leaves the
    /// workspace silently pointed at a model nobody approved.
    /// </summary>
    public async Task<AiGovernanceDto> UpdateGovernanceAsync(UpdateAiGovernanceCommand command, CancellationToken ct)
    {
        var (settings, exists) = await LoadAsync(ct);
        if (!exists)
        {
            store.Add(settings);
        }

        settings.UpdateGovernance(
            command.AllowedModels ?? [], command.RedactEmails, command.RedactApiKeys, command.RedactCreditCards,
            command.CustomRedactionPatterns ?? [], Now);

        Audit("ai.provider_settings.governance_updated", "AiProviderSettings", settings.Id,
            new { AllowedModelCount = settings.AllowedModels.Count, settings.RedactEmails, settings.RedactApiKeys, settings.RedactCreditCards });
        await SaveAsync(ct);
        return ToGovernanceDto(settings);
    }

    private static AiGovernanceDto ToGovernanceDto(AiProviderSettings s)
        => new(s.AllowedModels, s.RedactEmails, s.RedactApiKeys, s.RedactCreditCards, s.CustomRedactionPatterns);

    /// <summary>Admin-gated load. Returns a transient default (not yet added) when the workspace has none.</summary>
    private async Task<(AiProviderSettings Settings, bool Exists)> LoadAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AiAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var settings = await store.FindAsync(workspaceId, ct);
        return settings is null
            ? (AiProviderSettings.CreateDefault(NewId(), workspaceId, Now), false)
            : (settings, true);
    }

    private AiProviderSettingsDto ToDto(AiProviderSettings s)
        => new(s.BaseUrl, s.Model, AiProviderSettings.Mask(protector.Unprotect(s.ApiKeyEncrypted)), s.IsEnabled);
}
