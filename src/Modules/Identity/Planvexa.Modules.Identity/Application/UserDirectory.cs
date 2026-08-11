namespace Planvexa.Modules.Identity.Application;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Identity.Domain;
using Planvexa.SharedContracts.Tenancy;
using Planvexa.SharedContracts.Users;

/// <summary>Implements the cross-module <see cref="IUserDirectory"/> contract.</summary>
public sealed class UserDirectory(
    IUserStore users,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    IInvitationDirectoryQuery invitations,
    IConfiguration configuration) : IUserDirectory
{
    public async Task<UserInfo> GetOrProvisionAsync(
        string subject,
        string email,
        string displayName,
        CancellationToken cancellationToken = default)
        => await GetOrProvisionAsync(subject, email, displayName, enforceRegistrationGate: true, cancellationToken);

    /// <summary>
    /// Same as the public overload, but lets trusted, config-driven callers (the first-run bootstrap
    /// admin) skip the self-registration gate below — that gate exists to stop arbitrary external
    /// identities from provisioning themselves via HTTP/SignalR, not to block an explicitly configured
    /// admin account.
    /// </summary>
    public async Task<UserInfo> GetOrProvisionAsync(
        string subject,
        string email,
        string displayName,
        bool enforceRegistrationGate,
        CancellationToken cancellationToken = default)
    {
        Guard.AgainstNullOrWhiteSpace(subject, nameof(subject));

        var user = await users.FindBySubjectAsync(subject, cancellationToken);
        if (user is null && !string.IsNullOrWhiteSpace(email))
        {
            // Unknown subject but known IdP-verified email: adopt the existing user instead of
            // colliding on the unique email index. Keycloak is the single trusted IdP here.
            user = await users.FindByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
            user?.LinkSubject(subject, clock.UtcNow);
        }

        var isNewUser = false;
        if (user is null)
        {
            var allowSelfRegistration = !bool.TryParse(configuration["Registration:AllowSelfRegistration"], out var configured) || configured;
            if (enforceRegistrationGate && !allowSelfRegistration && !await invitations.HasPendingInvitationAsync(email, cancellationToken))
            {
                throw new ForbiddenException("Self-registration is disabled. Ask a workspace admin to invite you.");
            }

            user = User.Provision(ids.NewId(), subject, email, displayName, clock.UtcNow);
            users.Add(user);
            isNewUser = true;
        }
        else
        {
            user.SyncProfile(email, displayName, clock.UtcNow);
        }

        user.MarkSeen(clock.UtcNow);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (isNewUser)
        {
            // Several parallel authenticated requests for the same brand-new subject/email (e.g. the
            // frontend firing /users/me, /workspaces/me, /features back-to-back right after a fresh
            // sign-in, or a SignalR connection racing the HTTP request) can all pass the "not found"
            // checks above before any of them commits. The subject/email unique indexes make every
            // loser's insert fail instead of silently duplicating the row (that duplication is exactly
            // what happened before those indexes existed — see the dedup migration). Discard this
            // request's losing attempt and adopt whichever row actually won.
            users.Discard(user);
            var winner = await users.FindBySubjectAsync(subject, cancellationToken)
                ?? await users.FindByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
            if (winner is null)
            {
                throw;
            }

            user = winner;
        }

        return ToInfo(user);
    }

    public async Task<UserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        return user is null ? null : ToInfo(user);
    }

    public async Task<UserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        return user is null ? null : ToInfo(user);
    }

    private static UserInfo ToInfo(User user) => new(user.Id, user.Email, user.DisplayName, user.AvatarUrl, user.Timezone, user.Locale, user.Theme);
}
