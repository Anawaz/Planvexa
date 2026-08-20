namespace Planvexa.Infrastructure.HostAdmin;

using Microsoft.EntityFrameworkCore;
using Planvexa.Infrastructure.Persistence;

/// <summary>
/// Runs a read with an explicitly EMPTY <c>app.current_workspace</c> and an explicitly stamped
/// <c>app.current_user</c>, on one connection held open for the duration.
///
/// The sibling of <c>TenancySessionGuard</c> (see its doc comment for the connection-pool race this
/// closes): <see cref="WorkspaceConnectionInterceptor"/> stamps both session variables when a
/// connection opens, but EF opens and closes a connection per command, so a pooled connection handed
/// back for the next command may still carry a stale stamp from a different logical scope. For
/// workspace-scoped reads that fails closed — a stale workspace filters rows out. For host-admin
/// reads it is worse than closed: it is *inconsistent*, because a stale non-empty workspace turns the
/// lenient "ambient workspace unset -&gt; all rows" policies (audit_isolation, 0029;
/// feature_entitlement_isolation, 0001) into single-workspace filters, so the console would show
/// different data depending on which pooled connection it happened to get.
///
/// Stamping <c>app.current_user</c> here too is what the host-admin policies from script 0094 read:
/// they re-check <c>identity.users.is_host_admin</c> for that id, so the database — not just the
/// application's authorization policy — decides whether these cross-workspace rows are visible.
/// </summary>
internal static class HostAdminSession
{
    public static async Task<T> WithoutWorkspaceAsync<T>(
        PlanvexaDbContext db, Guid userId, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        var user = userId.ToString();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_user', {user}, false), set_config('app.current_workspace', '', false)",
                cancellationToken);
            return await read();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
