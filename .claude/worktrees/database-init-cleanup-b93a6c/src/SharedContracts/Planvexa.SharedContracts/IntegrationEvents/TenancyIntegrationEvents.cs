namespace Planvexa.SharedContracts.IntegrationEvents;

/// <summary>Published when a new workspace is created (either as first-run onboarding or as an
/// additional workspace for an already-authenticated user).</summary>
public sealed record WorkspaceCreatedIntegrationEvent(
    Guid WorkspaceId,
    string WorkspaceName,
    Guid CreatedByUserId) : IntegrationEvent;

/// <summary>Published when a member is invited to a workspace.</summary>
public sealed record MemberInvitedIntegrationEvent(
    Guid WorkspaceId,
    Guid InvitationId,
    string Email,
    string Role,
    Guid InvitedByUserId) : IntegrationEvent;

/// <summary>Published when an invitation is accepted and a membership is created.</summary>
public sealed record InvitationAcceptedIntegrationEvent(
    Guid WorkspaceId,
    Guid InvitationId,
    Guid MembershipId,
    Guid UserId) : IntegrationEvent;
