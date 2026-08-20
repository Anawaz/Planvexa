namespace Planvexa.Modules.Tenancy.Application;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Platform;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Tenancy.Domain;
using Planvexa.SharedContracts.Platform;
using Planvexa.SharedContracts.Users;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Creates a brand-new, independent top-level Workspace: an Owner membership for the founder, and
/// seeded workspace feature defaults — all in a single transaction. Workspace is the sole top-level
/// business boundary (AGENTS.md: "There is no Organization/Tenant layer"), so this is the ONLY
/// workspace-creation path — used identically for first-run onboarding (no ambient Workspace yet) and
/// for an already-authenticated user adding another Workspace to their account.
/// </summary>
public sealed class WorkspaceRegistrationService(
    IWorkspaceStore workspaces,
    IMembershipStore memberships,
    IRoleStore roles,
    IIdGenerator ids,
    IClock clock,
    IAuditWriter audit,
    IUnitOfWork unitOfWork,
    IWorkspaceContextAccessor workspaceAccessor,
    IFeatureEntitlementStore entitlements,
    IWorkspaceProvisioner workspaceProvisioner,
    IInstanceSettingsProvider instanceSettings,
    IUserDirectory users)
{
    /// <summary>
    /// Enforces the installation's workspace-creation policy. Checked here rather than at the endpoint
    /// because this is the ONLY workspace-creation path in the product (see the class doc comment), so
    /// one check covers onboarding, "add another workspace", and any future caller.
    ///
    /// The first-run bootstrap is exempt by construction: it passes <paramref name="enforcePolicy"/>
    /// as false, the same shape as <c>IUserDirectory.GetOrProvisionAsync</c>'s registration-gate
    /// bypass, because a configured bootstrap admin creating the installation's first workspace is not
    /// the self-service path this policy governs.
    /// </summary>
    private async Task EnsureMayCreateAsync(Guid ownerUserId, bool enforcePolicy, CancellationToken cancellationToken)
    {
        if (!enforcePolicy)
        {
            return;
        }

        var settings = await instanceSettings.GetAsync(cancellationToken);
        if (settings.WorkspaceCreationPolicy != WorkspaceCreationPolicies.HostAdminsOnly)
        {
            return;
        }

        if (!await users.IsHostAdminAsync(ownerUserId, cancellationToken))
        {
            throw new ForbiddenException(
                "Workspace creation is restricted to host administrators on this instance.");
        }
    }

    /// <summary>
    /// The user asks for a Workspace name and optionally a slug. If no slug is provided, one is
    /// generated from the name. The creator becomes Owner, plan entitlements are seeded, and the
    /// starter Space/List/status-scheme are provisioned in one idempotent transaction. Retrying with
    /// the same name creates a distinct Workspace (a fresh internal id), never a duplicate structure.
    /// </summary>
    public async Task<WorkspaceDto> OnboardWorkspaceAsync(
        string workspaceName, Guid ownerUserId, string? workspaceSlug = null,
        CancellationToken cancellationToken = default, bool enforceCreationPolicy = true)
    {
        Guard.AgainstNullOrWhiteSpace(workspaceName, nameof(workspaceName));
        await EnsureMayCreateAsync(ownerUserId, enforceCreationPolicy, cancellationToken);

        var now = clock.UtcNow;
        var workspaceId = ids.NewId();
        string slug;
        if (workspaceSlug is null)
        {
            slug = await GenerateUniqueSlugAsync(workspaceName, cancellationToken);
        }
        else
        {
            slug = Workspace.NormalizeSlug(workspaceSlug);
            if (await workspaces.SlugExistsAsync(slug, cancellationToken))
            {
                throw new ConflictException($"A workspace with slug '{slug}' already exists.");
            }
        }

        var workspace = Workspace.Create(workspaceId, workspaceName, slug, ownerUserId, now);

        // The new workspace row must satisfy the hardened RLS WITH CHECK (0029) on write, but no
        // ambient workspace context exists yet for a brand-new workspace. Set it to the workspace being
        // created so PlanvexaDbContext.ReapplyWorkspaceSessionAsync pushes the matching
        // app.current_workspace GUC before insert.
        workspaceAccessor.Set(new WorkspaceContext(
            workspaceId, ownerUserId, membershipId: null, role: MembershipRole.Owner.ToString(),
            permissions: new HashSet<string>(), entitlements: new HashSet<string>(), correlationId: string.Empty));

        workspaces.Add(workspace);

        // Seed the five built-in roles (ADR-0003) before creating the Owner membership so it can
        // be linked to its role row from the start — RoleId is the authorization source of truth going
        // forward, the MembershipRole enum stays only as the compatibility fast path.
        var ownerRoleId = SeedBuiltInRoles(workspaceId, now);
        var ownerMembership = WorkspaceMember.Create(ids.NewId(), workspaceId, ownerUserId, MembershipRole.Owner, now, ownerRoleId);
        memberships.Add(ownerMembership);

        foreach (var grant in PlanCatalog.DefaultsForWorkspace())
        {
            entitlements.Add(FeatureEntitlement.Grant(
                ids.NewId(), workspaceId, grant.Key, grant.Enabled, grant.Limit, "self-hosted-default"));
        }

        await workspaceProvisioner.ProvisionDefaultsAsync(workspaceId, cancellationToken);

        audit.Write("workspace.onboarded", nameof(Workspace), workspaceId, new { workspaceName, slug });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new WorkspaceDto(workspaceId, workspace.Name, workspace.Slug, workspace.Status.ToString(), workspace.CreatedAtUtc, MembershipRole.Owner.ToString());
    }

    /// <summary>Seeds the five built-in roles (see <see cref="BuiltInRoles"/>) and returns the Owner role's id.</summary>
    private Guid SeedBuiltInRoles(Guid workspaceId, DateTimeOffset nowUtc)
    {
        var ownerRoleId = Guid.Empty;
        foreach (var definition in BuiltInRoles.All)
        {
            var roleId = ids.NewId();
            roles.Add(Role.CreateBuiltIn(roleId, workspaceId, definition.Key, definition.Name, nowUtc));
            foreach (var permission in definition.Permissions)
            {
                roles.AddPermission(RolePermission.Grant(workspaceId, roleId, permission));
            }

            if (definition.Role == MembershipRole.Owner)
            {
                ownerRoleId = roleId;
            }
        }

        return ownerRoleId;
    }

    private async Task<string> GenerateUniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = SlugGenerator.Generate(name, "workspace");
        var candidate = baseSlug;
        for (var attempt = 0; attempt < 6 && await workspaces.SlugExistsAsync(candidate, cancellationToken); attempt++)
        {
            candidate = SlugGenerator.Generate($"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(baseSlug.Length + 7, 40)], "workspace");
        }

        return candidate;
    }
}
