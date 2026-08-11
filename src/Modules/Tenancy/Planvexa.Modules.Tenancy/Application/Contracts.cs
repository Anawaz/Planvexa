namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.Modules.Tenancy.Domain;

// ---- Commands ----

public sealed record CreateWorkspaceCommand(string Name, string Slug);

public sealed record InviteMemberCommand(Guid WorkspaceId, string Email, MembershipRole Role);

public sealed record ChangeMemberRoleCommand(Guid WorkspaceId, Guid MembershipId, MembershipRole Role);

public sealed record TransferOwnershipCommand(Guid WorkspaceId, Guid MembershipId);

// ---- Read models ----

public sealed record WorkspaceDto(Guid Id, string Name, string Slug, string Status, DateTimeOffset CreatedAtUtc, string Role);

public sealed record MemberDto(Guid Id, Guid UserId, string Role, string Status, bool IsGuest, DateTimeOffset JoinedAtUtc);

public sealed record TeamDto(Guid Id, Guid WorkspaceId, string Name, string? Description, bool IsArchived, int MemberCount);

public sealed record TeamMemberDto(Guid UserId, DateTimeOffset AddedAtUtc);

public sealed record CreateTeamCommand(Guid WorkspaceId, string Name, string? Description);

public sealed record UpdateTeamCommand(string Name, string? Description);

public sealed record FeatureDto(string Key, bool Enabled, long? Limit, string Source);

/// <summary>A workspace role and its granted permission keys (ADR-0003, read-only for now).</summary>
public sealed record RoleDto(Guid Id, string Key, string Name, bool IsBuiltIn, IReadOnlySet<string> Permissions);

/// <summary>
/// Result of creating or resending an invitation. Deliberately carries NO raw token: the token is a
/// credential and is delivered only as a signed link in the invitation email (AGENTS.md invitation
/// security). The members UI never needs it.
/// </summary>
public sealed record InvitationCreatedDto(Guid InvitationId, string Email, string Role, DateTimeOffset ExpiresAtUtc);

/// <summary>Everything a delivery adapter needs to send an invitation as a signed email link.</summary>
public sealed record InvitationEmailMessage(
    Guid WorkspaceId,
    Guid InvitationId,
    string WorkspaceName,
    string Email,
    string Role,
    string RawToken,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Delivers an invitation to the invited email address as a signed, single-use link. The raw token is
/// passed ONLY to this port (to build the link) and is never returned from the API. The concrete
/// adapter lives in the host composition root (SMTP in dev/prod, logging sink in tests).
/// </summary>
public interface IInvitationEmailSender
{
    Task SendInvitationAsync(InvitationEmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Pending invitation projection for the members UI — deliberately carries no raw token.</summary>
public sealed record PendingInvitationDto(Guid Id, string Email, string Role, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

public sealed record InvitationAcceptedDto(Guid MembershipId, Guid WorkspaceId, string Role);

// ---- Workspace resolution ----

/// <summary>Result of resolving a Workspace + the caller's role within it (used by middleware/hubs).</summary>
public sealed record WorkspaceResolution(
    Guid WorkspaceId,
    string Slug,
    MembershipRole Role,
    IReadOnlySet<string> EnabledFeatures,
    bool RequiresMfa);

/// <summary>
/// Resolves the Workspace context for an authenticated user from a Workspace id, validating that the
/// user is actually an active member. Bypasses the Workspace query filter internally (bootstrap lookup
/// — this runs BEFORE a Workspace context exists).
/// </summary>
public interface IWorkspaceResolver
{
    Task<WorkspaceResolution?> ResolveByWorkspaceIdAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default);

    Task<bool> CanAccessWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken cancellationToken = default);
}
