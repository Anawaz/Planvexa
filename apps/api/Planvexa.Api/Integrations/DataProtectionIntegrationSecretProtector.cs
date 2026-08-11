namespace Planvexa.Api.Integrations;

using Microsoft.AspNetCore.DataProtection;
using Planvexa.Modules.Integrations.Application;

/// <summary>
/// Encrypts a workspace's third-party integration provider credential at rest with ASP.NET Core Data
/// Protection — the Integrations-module sibling of <c>Planvexa.Api.Ai.DataProtectionAiSecretProtector</c>
/// (module boundaries mean Integrations cannot reuse Ai's copy directly). Same purpose-string-scoped
/// protector, same lost-key-ring behavior (never throws; a lost key ring just means an admin re-enters
/// the credential).
/// </summary>
// ponytail: uses the default key ring (file system, %LOCALAPPDATA% / ~/.aspnet), same as the Ai sibling —
// see that file's doc comment for the multi-instance/ephemeral-container upgrade path.
public sealed class DataProtectionIntegrationSecretProtector(IDataProtectionProvider provider) : IIntegrationSecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("Planvexa.Integrations.ProviderSecret.v1");

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
