namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Identity.Application;
using Planvexa.Modules.Identity.Domain;

internal sealed class UserStore(PlanvexaDbContext db) : IUserStore
{
    public void Add(User user) => db.Users.Add(user);

    public void Discard(User user) => db.Entry(user).State = EntityState.Detached;

    public Task<User?> FindBySubjectAsync(string subject, CancellationToken cancellationToken = default)
        => db.Users.FirstOrDefaultAsync(u => u.Subject == subject, cancellationToken);

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
        => db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<int> CountHostAdminsAsync(CancellationToken cancellationToken = default)
        => db.Users.CountAsync(u => u.IsHostAdmin && u.IsActive, cancellationToken);
}
