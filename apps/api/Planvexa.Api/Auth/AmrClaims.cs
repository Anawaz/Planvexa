namespace Planvexa.Api.Auth;

using System.Security.Claims;

/// <summary>
/// Shared "amr" (Authentication Method Reference, RFC 8176) evaluation for every place identity is
/// resolved from a <see cref="ClaimsPrincipal"/> — currently <c>UserContextMiddleware</c> (HTTP) and
/// <c>WorkspaceHub</c> (SignalR, which runs in its own DI scope and never passes through the HTTP
/// middleware pipeline). Kept in one place so both enforce the exact same "what counts as MFA" set.
/// </summary>
public static class AmrClaims
{
    /// <summary>"otp" is the value Keycloak's OTP Form execution is configured to emit (see the AMR
    /// protocol mapper in scripts/keycloak-bootstrap.ps1); the others are accepted so hardware
    /// keys/WebAuthn count too if a Workspace later enables them.</summary>
    private static readonly HashSet<string> MfaValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "otp", "hwk", "sms", "u2f", "mfa", "webauthn",
    };

    /// <summary>True if the principal's "amr" claims (a JSON-array claim surfaces as multiple
    /// same-named Claims after JWT deserialization) include a recognized second-factor value.</summary>
    public static bool HasVerifiedMfa(ClaimsPrincipal principal)
        => principal.FindAll("amr").Any(c => MfaValues.Contains(c.Value));
}
