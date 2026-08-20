namespace Planvexa.Modules.Identity.Application;

using Planvexa.Modules.Identity.Domain;

/// <summary>Persistence abstraction for <see cref="User"/>, implemented in the Infrastructure project.</summary>
public interface IUserStore
{
    Task<User?> FindBySubjectAsync(string subject, CancellationToken cancellationToken = default);
    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many active instance-level administrators exist (see <see cref="User.IsHostAdmin"/>).
    /// Backs two callers: the first-run bootstrap's "does this installation have a host admin yet?"
    /// check, and the console's refusal to demote or disable the last one.
    /// </summary>
    Task<int> CountHostAdminsAsync(CancellationToken cancellationToken = default);

    void Add(User user);

    /// <summary>Stops tracking a user that failed to persist (e.g. lost a concurrent-create race against
    /// the subject/email unique indexes), so it is not retried on the scope's next SaveChanges call.</summary>
    void Discard(User user);
}
