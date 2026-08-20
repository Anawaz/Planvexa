namespace Planvexa.Modules.Identity.Application;

using Microsoft.EntityFrameworkCore;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Identity.Domain;
using Planvexa.SharedContracts.Platform;
using Planvexa.SharedContracts.Tenancy;
using Planvexa.SharedContracts.Users;

/// <summary>Implements the cross-module <see cref="IUserDirectory"/> contract.</summary>
public sealed class UserDirectory(
    IUserStore users,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock,
    IInvitationDirectoryQuery invitations,
    IInstanceSettingsProvider instanceSettings) : IUserDirectory
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
            if (user is not null && user.IsActive)
            {
                // Only re-key a live account. Adopting a disabled one would hand its identity to the
                // new subject — the guard below rejects the request either way, but this keeps the
                // rejection from leaving a mutated entity behind.
                user.LinkSubject(subject, clock.UtcNow);
            }
        }

        // A disabled account gets no further than here. This is the ONE path every authenticated
        // request takes — UserContextMiddleware calls it before anything downstream runs, and the
        // SignalR hub handshake passes through the same middleware — so one check closes every
        // endpoint at once instead of each having to remember. Nothing is written for a rejected
        // caller: no profile sync, no MarkSeen, no SaveChanges.
        //
        // Covers both ways IsActive can be false: a host administrator's User.Deactivate, and
        // User.Anonymize (GDPR deletion), which has always cleared IsActive but until now had nothing
        // enforcing it.
        if (user is not null && !user.IsActive)
        {
            throw new ForbiddenException("This account has been disabled. Contact your administrator.");
        }

        var isNewUser = false;
        if (user is null)
        {
            // The live value is the instance settings row, editable by a host administrator in the
            // console. Registration:AllowSelfRegistration is now only the seed default for that row on
            // a fresh installation (see InstanceSettingsService.LoadAsync) — so an operator no longer
            // has to redeploy to open or close registration.
            var allowSelfRegistration = (await instanceSettings.GetAsync(cancellationToken)).AllowSelfRegistration;
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

    public async Task<bool> IsHostAdminAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        return user is { IsHostAdmin: true, IsActive: true };
    }

    public async Task<UserInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        return user is null ? null : ToInfo(user);
    }

    private static UserInfo ToInfo(User user) => new(user.Id, user.Email, user.DisplayName, user.AvatarUrl, user.Timezone, user.Locale, user.Theme);
}
