namespace Planvexa.Modules.Governance.Domain;

using System.Security.Cryptography;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>Enterprise security configuration for a workspace.</summary>
public sealed class EnterpriseSecuritySettings : Entity, IWorkspaceOwned
{
    private EnterpriseSecuritySettings()
    {
    }

    private EnterpriseSecuritySettings(Guid id, Guid workspaceId, DateTimeOffset updatedAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public bool SsoEnabled { get; private set; }
    public string? SamlEntityId { get; private set; }
    public string? SamlMetadataUrl { get; private set; }
    public bool ScimEnabled { get; private set; }
    public string? ScimTokenHash { get; private set; }
    public bool MfaRequired { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool ScimTokenSet => ScimTokenHash is not null;

    public static EnterpriseSecuritySettings CreateDefault(Guid id, Guid workspaceId, DateTimeOffset nowUtc)
        => new(id, workspaceId, nowUtc);

    public void Update(
        bool? ssoEnabled,
        string? samlEntityId,
        string? samlMetadataUrl,
        bool? scimEnabled,
        bool? mfaRequired,
        DateTimeOffset nowUtc)
    {
        var nextSsoEnabled = ssoEnabled ?? SsoEnabled;
        var nextSamlEntityId = samlEntityId is null ? SamlEntityId : samlEntityId.Trim();
        var nextSamlMetadataUrl = samlMetadataUrl is null ? SamlMetadataUrl : samlMetadataUrl.Trim();
        var nextScimEnabled = scimEnabled ?? ScimEnabled;
        var nextMfaRequired = mfaRequired ?? MfaRequired;

        if (samlMetadataUrl is not null && !IsAbsoluteHttpUrl(nextSamlMetadataUrl))
        {
            throw new ValidationAppException("SAML metadata URL must be an absolute HTTP or HTTPS URL.");
        }

        if (nextSsoEnabled && (string.IsNullOrWhiteSpace(nextSamlEntityId) || string.IsNullOrWhiteSpace(nextSamlMetadataUrl)))
        {
            throw new ValidationAppException("SAML entity id and metadata URL are required when SSO is enabled.");
        }

        SsoEnabled = nextSsoEnabled;
        SamlEntityId = nextSamlEntityId;
        SamlMetadataUrl = nextSamlMetadataUrl;
        ScimEnabled = nextScimEnabled;
        MfaRequired = nextMfaRequired;
        UpdatedAtUtc = nowUtc;
    }

    public void SetScimToken(string rawToken, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw new ValidationAppException("SCIM token is required.");
        }

        ScimTokenHash = HashToken(rawToken);
        UpdatedAtUtc = nowUtc;
    }

    public void ClearScimToken() => ScimTokenHash = null;

    private static bool IsAbsoluteHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(bytes);
    }
}

