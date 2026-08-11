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
}
