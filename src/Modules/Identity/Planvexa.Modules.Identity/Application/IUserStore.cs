namespace Planvexa.Modules.Identity.Application;

using Planvexa.Modules.Identity.Domain;

/// <summary>Persistence abstraction for <see cref="User"/>, implemented in the Infrastructure project.</summary>
public interface IUserStore
{
    Task<User?> FindBySubjectAsync(string subject, CancellationToken cancellationToken = default);
    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    void Add(User user);

    /// <summary>Stops tracking a user that failed to persist (e.g. lost a concurrent-create race against
    /// the subject/email unique indexes), so it is not retried on the scope's next SaveChanges call.</summary>
    void Discard(User user);
}
