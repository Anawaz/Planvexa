namespace Planvexa.BuildingBlocks.Abstractions;

/// <summary>
/// The authenticated principal for the current request, derived from the validated OIDC token
/// (Keycloak) or the development auth handler. This is identity only — authorization and tenant
/// membership are decided by the application (see ADR-0003).
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Application user id (mapped from the external subject).</summary>
    Guid UserId { get; }

    /// <summary>External identity provider subject (Keycloak 'sub').</summary>
    string Subject { get; }

    string Email { get; }

    string DisplayName { get; }

    /// <summary>
    /// True if this request's token carries an "amr" (Authentication Method Reference, RFC 8176) value
    /// recognized as multi-factor (e.g. "otp") — see <c>UserContextMiddleware</c> for the exact claim
    /// read and <c>WorkspaceResolutionMiddleware</c> for where a Workspace's MfaRequired setting is
    /// enforced against this flag. False for a token/session with only a password factor.
    /// </summary>
    bool HasVerifiedMfa { get; }
}
