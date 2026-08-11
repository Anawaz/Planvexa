namespace Planvexa.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Connection for background work that legitimately spans workspaces with no request context (outbox
/// drain, notification delivery, recurring generation, export/retention sweeps). Hardened RLS
/// (script 0029) treats a missing <c>app.current_workspace</c> as "no rows", so under the production
/// non-superuser application role those sweeps would silently read nothing and have their writes
/// rejected. Rather than granting the application role BYPASSRLS, those paths switch their
/// <see cref="PlanvexaDbContext"/> to an optional privileged connection
/// (<c>ConnectionStrings:PlanvexaMaintenance</c>). When it is not configured this is a no-op and the
/// main connection is used, which is what single-role deployments and superuser-backed tests want.
/// </summary>
public sealed class MaintenanceConnection(string? connectionString)
{
    public void ApplyTo(PlanvexaDbContext db)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            db.Database.SetConnectionString(connectionString);
        }
    }

    /// <summary>
    /// Runs a single lookup keyed by a globally unique secret (invitation token, share-link token,
    /// public form token, personal access token) on the maintenance connection, then restores the
    /// normal one. Such a lookup has to span workspaces because it is what establishes the workspace
    /// in the first place; everything the caller does afterwards binds the resolved workspace and
    /// runs under the RLS-enforced application connection.
    /// </summary>
    public async Task<T> LookupAsync<T>(PlanvexaDbContext db, Func<Task<T>> lookup)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return await lookup();
        }

        var original = db.Database.GetConnectionString();
        db.Database.SetConnectionString(connectionString);
        try
        {
            return await lookup();
        }
        finally
        {
            db.Database.SetConnectionString(original);
        }
    }
}

public static class MaintenanceScopeExtensions
{
    /// <summary>
    /// Switches the scope's <see cref="PlanvexaDbContext"/> to the maintenance connection. Must be
    /// called before the scope issues its first query.
    /// </summary>
    public static IServiceScope UseMaintenanceConnection(this IServiceScope scope)
    {
        scope.ServiceProvider.GetRequiredService<MaintenanceConnection>()
            .ApplyTo(scope.ServiceProvider.GetRequiredService<PlanvexaDbContext>());
        return scope;
    }
}
