namespace Planvexa.Modules.Identity.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.Modules.Identity.Domain;
using Planvexa.SharedContracts.Users;

/// <summary>Implements the cross-module <see cref="IUserDirectory"/> contract.</summary>
public sealed class UserDirectory(
    IUserStore users,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : IUserDirectory
{
    public async Task<UserInfo> GetOrProvisionAsync(
        string subject,
        string email,
        string displayName,
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

        if (user is null)
        {
            user = User.Provision(ids.NewId(), subject, email, displayName, clock.UtcNow);
            users.Add(user);
        }
        else
        {
            user.SyncProfile(email, displayName, clock.UtcNow);
        }

        user.MarkSeen(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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

    private static UserInfo ToInfo(User user) => new(user.Id, user.Email, user.DisplayName);
}
