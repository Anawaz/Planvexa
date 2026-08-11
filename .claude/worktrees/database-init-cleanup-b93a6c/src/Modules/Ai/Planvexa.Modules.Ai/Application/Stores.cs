namespace Planvexa.Modules.Ai.Application;

using Planvexa.Modules.Ai.Domain;

public interface IAiRequestStore
{
    void Add(AiRequest request);
    Task<AiRequest?> FindByKeyAsync(Guid workspaceId, string requestKey, CancellationToken ct = default);
    Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<long> SumTokensForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IAiProviderSettingsStore
{
    void Add(AiProviderSettings settings);
    Task<AiProviderSettings?> FindAsync(Guid workspaceId, CancellationToken ct = default);
}

/// <summary>
/// Reversible protection for the stored provider API key. Implemented by the host (ASP.NET Core Data
/// Protection) so the module stays free of hosting dependencies (AGENTS.md rule 7).
/// </summary>
public interface IAiSecretProtector
{
    string Protect(string plaintext);

    /// <summary>Returns the plaintext, or empty when the value is absent or cannot be decrypted.</summary>
    string Unprotect(string protectedValue);
}
