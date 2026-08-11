namespace Planvexa.Modules.Ai.Application;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Ai.Domain;

public interface IAiRequestStore
{
    void Add(AiRequest request);
    Task<AiRequest?> FindByKeyAsync(Guid workspaceId, string requestKey, CancellationToken ct = default);
    Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<long> SumTokensForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Estimated tokens recorded for the workspace at or after <paramref name="sinceUtc"/> — used to
    /// enforce <see cref="AiProviderSettings.CreditLimit"/> against the current calendar month.</summary>
    Task<long> SumTokensForWorkspaceSinceAsync(Guid workspaceId, DateTimeOffset sinceUtc, CancellationToken ct = default);
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

/// <summary>
/// Shared enforcement of <see cref="AiProviderSettings.CreditLimit"/>, called by every dispatch point that
/// invokes <see cref="Planvexa.SharedContracts.Ai.IAiCompletionProvider"/> for a real (cost-incurring)
/// provider call — currently <c>AiAssistService</c> (Ai module) and <c>WorkspaceQaService</c> (apps/api,
/// composed there because it also depends on the cross-module search aggregator). A single shared check
/// means a workspace's monthly cap applies uniformly regardless of which entry point is used.
/// </summary>
public static class AiCreditLimitGuard
{
    /// <summary>
    /// Throws <see cref="CreditLimitExceededException"/> once the workspace has spent its monthly (calendar
    /// month, UTC) token allowance. A workspace with no usable provider configured never reaches a real
    /// provider call (it always falls back to the free offline extractive provider), so it is exempt
    /// regardless of the limit. A null limit (the default) is unlimited.
    /// </summary>
    public static async Task EnsureWithinLimitAsync(
        IAiProviderSettingsStore settingsStore, IAiRequestStore requests, Guid workspaceId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var settings = await settingsStore.FindAsync(workspaceId, ct);
        if (settings is not { IsUsable: true, CreditLimit: { } limit })
        {
            return;
        }

        var monthStartUtc = new DateTimeOffset(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var usedThisMonth = await requests.SumTokensForWorkspaceSinceAsync(workspaceId, monthStartUtc, ct);
        if (usedThisMonth >= limit)
        {
            throw new CreditLimitExceededException(
                $"This workspace has reached its monthly AI credit limit of {limit} estimated tokens. Contact a workspace admin to raise it.");
        }
    }
}
