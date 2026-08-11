namespace Planvexa.Modules.Identity.Application;

using Planvexa.Modules.Identity.Domain;

/// <summary>Persistence abstraction for <see cref="User"/>, implemented in the Infrastructure project.</summary>
public interface IUserStore
{
    Task<User?> FindBySubjectAsync(string subject, CancellationToken cancellationToken = default);
    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    void Add(User user);
}
