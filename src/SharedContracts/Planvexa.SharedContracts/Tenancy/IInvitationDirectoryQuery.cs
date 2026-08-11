namespace Planvexa.SharedContracts.Tenancy;

/// <summary>
/// Contract (implemented by the Tenancy module) exposing whether an email has a pending workspace
/// invitation, so other modules can gate new-account provisioning without depending on Tenancy
/// internals (AGENTS.md rule 7).
/// </summary>
public interface IInvitationDirectoryQuery
{
    Task<bool> HasPendingInvitationAsync(string email, CancellationToken cancellationToken = default);
}
