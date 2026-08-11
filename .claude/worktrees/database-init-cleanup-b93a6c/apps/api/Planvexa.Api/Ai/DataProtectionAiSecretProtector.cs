namespace Planvexa.Api.Ai;

using Microsoft.AspNetCore.DataProtection;
using Planvexa.Modules.Ai.Application;

/// <summary>
/// Encrypts the per-tenant AI provider API key at rest with ASP.NET Core Data Protection, so the module
/// never sees hosting concerns and no bespoke key management is introduced.
/// </summary>
// ponytail: uses the default key ring (file system, %LOCALAPPDATA% / ~/.aspnet). Multi-instance or
// ephemeral-container deployments need PersistKeysToX + ProtectKeysWithY in Program.cs; until then a lost
// key ring just means admins re-enter their API key (Unprotect returns empty, never throws).
public sealed class DataProtectionAiSecretProtector(IDataProtectionProvider provider) : IAiSecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("Planvexa.Ai.ProviderApiKey.v1");

    public string Protect(string plaintext)
        => string.IsNullOrEmpty(plaintext) ? string.Empty : this.protector.Protect(plaintext);

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            return this.protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return string.Empty;
        }
    }
}
