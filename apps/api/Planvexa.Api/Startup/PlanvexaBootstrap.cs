namespace Planvexa.Api.Startup;

using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Database;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.SharedContracts.Users;

/// <summary>
/// First-run bootstrap, run on every environment after DbUp (and after the Development-only demo
/// seed). A freshly created database has no users and no workspaces, so nothing — not even signing in
/// — has anywhere to land. This provisions exactly one admin user and one workspace from
/// configuration, then never touches the database again.
///
/// It is deliberately NOT the demo seeder: <see cref="Planvexa.Database.PlanvexaDevelopmentSeeder"/>
/// creates fixed demo accounts and sample content and stays gated behind
/// <c>Database:SeedDevelopmentData</c>. This creates the minimum a real install needs, which is why it
/// is safe to leave on in production.
///
/// The admin user is keyed by <c>Bootstrap:AdminSubject</c> — the identity-provider subject. Signing
/// in as that admin also requires a matching Keycloak account; <c>scripts/keycloak-bootstrap.ps1</c>
/// creates one from the same values.
/// </summary>
public static class PlanvexaBootstrap
{
    public static async Task EnsureAdminWorkspaceAsync(
        WebApplication app, string connectionString, bool seededDevelopmentData, CancellationToken cancellationToken = default)
    {
        var config = app.Configuration.GetSection("Bootstrap");
        if (!config.GetValue("Enabled", true))
        {
            app.Logger.LogInformation("First-run bootstrap disabled (Bootstrap:Enabled=false).");
            return;
        }

        // The demo seed and this bootstrap are alternatives, not additions: the seed already leaves a
        // usable install behind, and its dev-admin account holds the same default email this would
        // claim (which GetOrProvisionAsync would then adopt, re-keying dev-admin's subject and breaking
        // that login). Whichever ran first owns the database.
        if (seededDevelopmentData)
        {
            app.Logger.LogInformation("First-run bootstrap skipped: the development seed already provisioned users and workspaces.");
            return;
        }

        var subject = config.GetValue("AdminSubject", "planvexa-admin")!;
        var email = config.GetValue("AdminEmail", "admin@planvexa.local")!;
        var displayName = config.GetValue("AdminDisplayName", "Planvexa Admin")!;
        var workspaceName = config.GetValue("WorkspaceName", "Planvexa")!;

        // Replicas start together on a first install (the chart defaults to two), and "no workspace yet"
        // is true for all of them at once — without this they would each create one. Everything below
        // runs inside the lock, so the replica that waited re-reads a database the winner already wrote.
        await using var startupLock = await PlanvexaDatabase.AcquireStartupLockAsync(connectionString, cancellationToken);

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        // Idempotent by construction: adopts an existing user with the same subject or IdP-verified
        // email. identity.users is a global table with no RLS policies, so this runs with no ambient
        // workspace.
        var directory = services.GetRequiredService<IUserDirectory>();
        var user = await directory.GetOrProvisionAsync(subject, email, displayName, enforceRegistrationGate: false, cancellationToken);

        // Load-bearing, not decorative. The connection interceptor turns the accessor's UserId into
        // the app.current_user session variable, which is the sole predicate of the bootstrap_member_read
        // and bootstrap_workspace_read RLS policies (scripts 0020 and 0026). Without it the membership
        // lookup below returns zero rows under hardened RLS even when workspaces exist, and every
        // restart would create another workspace.
        services.GetRequiredService<IWorkspaceContextAccessor>().Set(new WorkspaceContext(
            workspaceId: Guid.Empty, userId: user.UserId, membershipId: null, role: string.Empty,
            permissions: new HashSet<string>(), entitlements: new HashSet<string>(), correlationId: string.Empty));
        services.GetRequiredService<CurrentUser>().Set(user.UserId, subject, user.Email, user.DisplayName);

        var workspaces = services.GetRequiredService<WorkspaceService>();
        var existing = await workspaces.ListForUserAsync(user.UserId, cancellationToken);
        if (existing.Count > 0)
        {
            app.Logger.LogInformation(
                "First-run bootstrap skipped: {Subject} is already a member of {Count} workspace(s).",
                subject, existing.Count);
            return;
        }

        // The same path the product's own onboarding uses (POST /api/v1/workspaces), so the bootstrap
        // workspace gets the five built-in roles, the Owner membership, plan entitlements and the
        // starter status scheme/space/list in one transaction.
        var onboarding = services.GetRequiredService<WorkspaceRegistrationService>();
        var workspace = await onboarding.OnboardWorkspaceAsync(workspaceName, user.UserId, cancellationToken: cancellationToken);

        app.Logger.LogInformation(
            "First-run bootstrap created workspace '{Name}' ({Slug}, {WorkspaceId}) owned by {Email} (subject {Subject}).",
            workspace.Name, workspace.Slug, workspace.Id, user.Email, subject);
    }
}
