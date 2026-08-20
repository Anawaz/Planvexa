namespace Planvexa.Api.Startup;

using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Database;
using Planvexa.Modules.Identity.Application;
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

        await EnsureHostAdminAsync(app, services, user.UserId, subject, cancellationToken);

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
        // enforceCreationPolicy: false — a WorkspaceCreationPolicy of HostAdminsOnly governs the
        // self-service path, not the configured bootstrap admin creating this installation's first
        // workspace. Same exemption shape as the registration gate bypass above.
        var workspace = await onboarding.OnboardWorkspaceAsync(
            workspaceName, user.UserId, cancellationToken: cancellationToken, enforceCreationPolicy: false);

        app.Logger.LogInformation(
            "First-run bootstrap created workspace '{Name}' ({Slug}, {WorkspaceId}) owned by {Email} (subject {Subject}).",
            workspace.Name, workspace.Slug, workspace.Id, user.Email, subject);
    }

    /// <summary>
    /// Seeds the first instance-level (host) administrator — the account that can reach
    /// <c>/api/v1/host/*</c> and the host console. Runs BEFORE the "already has a workspace" early
    /// return above on purpose: on an upgraded installation the configured admin virtually always has
    /// a workspace already, and gating the grant behind that return would mean an existing install
    /// could never produce its first host admin.
    ///
    /// Only ever grants when the installation has NO active host admin at all. Once one exists, host
    /// administration is self-administered through the console — so demoting the bootstrap account is
    /// permanent rather than being silently undone by the next restart.
    /// </summary>
    private static async Task EnsureHostAdminAsync(
        WebApplication app, IServiceProvider services, Guid userId, string subject, CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<IUserStore>();
        if (await store.CountHostAdminsAsync(cancellationToken) > 0)
        {
            return;
        }

        if (await store.FindByIdAsync(userId, cancellationToken) is not { } admin)
        {
            return;
        }

        admin.GrantHostAdmin(services.GetRequiredService<IClock>().UtcNow);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);

        app.Logger.LogInformation(
            "First-run bootstrap granted host administration to {Subject} — this installation had none.", subject);
    }
}
