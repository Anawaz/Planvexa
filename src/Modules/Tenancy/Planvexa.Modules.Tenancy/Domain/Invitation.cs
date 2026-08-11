namespace Planvexa.Modules.Tenancy.Domain;

using System.Security.Cryptography;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.SharedContracts.IntegrationEvents;

/// <summary>
/// A pending invitation to join a workspace. The raw token is returned to the caller exactly once;
/// only a SHA-256 hash is stored, so a database leak does not expose usable invitation links.
/// </summary>
public sealed class Invitation : Entity, IWorkspaceOwned
{
    private Invitation()
    {
    }

    private Invitation(
        Guid id, Guid workspaceId, string email, MembershipRole role,
        string tokenHash, Guid invitedByUserId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Email = email;
        Role = role;
        TokenHash = tokenHash;
        Status = InvitationStatus.Pending;
        InvitedByUserId = invitedByUserId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public MembershipRole Role { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public InvitationStatus Status { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }

    public static (Invitation Invitation, string RawToken) Create(
        Guid id, Guid workspaceId, string email, MembershipRole role,
        Guid invitedByUserId, DateTimeOffset nowUtc, TimeSpan validFor)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstNullOrWhiteSpace(email, nameof(email));

        var rawToken = GenerateToken();
        var invitation = new Invitation(
            id, workspaceId, email.Trim().ToLowerInvariant(), role,
            HashToken(rawToken), invitedByUserId, nowUtc, nowUtc.Add(validFor));
        invitation.Raise(new MemberInvitedIntegrationEvent(
            workspaceId, id, invitation.Email, role.ToString(), invitedByUserId));
        return (invitation, rawToken);
    }

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    public void Accept(Guid userId, Guid membershipId, DateTimeOffset nowUtc)
    {
        Status = InvitationStatus.Accepted;
        AcceptedByUserId = userId;
        AcceptedAtUtc = nowUtc;
        Raise(new InvitationAcceptedIntegrationEvent(WorkspaceId, Id, membershipId, userId));
    }

    public void Revoke() => Status = InvitationStatus.Revoked;

    public void MarkExpired() => Status = InvitationStatus.Expired;

    /// <summary>
    /// Rotates the token and re-opens the invitation (resend). The previous link stops working because
    /// only the new hash is stored; the fresh raw token is returned once for out-of-band delivery.
    /// </summary>
    public string Rotate(DateTimeOffset nowUtc, TimeSpan validFor)
    {
        var rawToken = GenerateToken();
        TokenHash = HashToken(rawToken);
        Status = InvitationStatus.Pending;
        CreatedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc.Add(validFor);
        AcceptedAtUtc = null;
        AcceptedByUserId = null;
        Raise(new MemberInvitedIntegrationEvent(
            WorkspaceId, Id, Email, Role.ToString(), InvitedByUserId));
        return rawToken;
    }

    public static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(bytes);
    }

    private static string GenerateToken()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexStringLower(buffer);
    }
}
