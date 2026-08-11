namespace Planvexa.UnitTests.Tenancy;

using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.Modules.Tenancy.Domain;
using Planvexa.SharedContracts.Workspaces;
using Shouldly;
using Xunit;

/// <summary>
/// ADR-0003: the ACL inheritance-walk resolver (<see cref="ResourcePermissionService.GetEffectiveAsync"/>).
/// Exercises the resolver in isolation against fakes — no DB. See the Integration suite for the
/// RLS/cross-workspace and end-to-end private-space visibility tests.
/// </summary>
public sealed class ResourcePermissionServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TeamId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    private static ResourcePermissionService BuildService(
        FakeResourcePermissionStore acl, FakeHierarchyQuery hierarchy, WorkspaceMember? member, IReadOnlyList<Guid>? teamIds = null)
    {
        var resolver = new RolePermissionResolver(new ThrowingRoleStore());
        return new ResourcePermissionService(
            acl,
            [hierarchy],
            new FakeMembershipStore(member),
            new FakeTeamStore(teamIds ?? []),
            resolver,
            new FakeIdGenerator(),
            new FakeClock(),
            new FakeAuditWriter());
    }

    [Fact]
    public async Task Direct_user_grant_is_honored()
    {
        var acl = new FakeResourcePermissionStore();
        var taskId = Guid.NewGuid();
        acl.Add("task", taskId, ResourcePrincipalType.User, UserId, ResourcePermissionLevel.Edit);

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("task", taskId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, null, null));

        var svc = BuildService(acl, hierarchy, member: null);
        var level = await svc.GetEffectiveAsync(WorkspaceId, UserId, "task", taskId);

        level.ShouldBe(PermissionLevel.Edit);
    }

    [Fact]
    public async Task Team_grant_is_honored_for_a_member_of_that_team()
    {
        var acl = new FakeResourcePermissionStore();
        var listId = Guid.NewGuid();
        acl.Add("list", listId, ResourcePrincipalType.Team, TeamId, ResourcePermissionLevel.Comment);

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("list", listId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, null, null));

        var svc = BuildService(acl, hierarchy, member: null, teamIds: [TeamId]);
        var level = await svc.GetEffectiveAsync(WorkspaceId, UserId, "list", listId);

        level.ShouldBe(PermissionLevel.Comment);
    }

    [Fact]
    public async Task Role_grant_is_honored_for_a_member_with_that_role_id()
    {
        var acl = new FakeResourcePermissionStore();
        var spaceId = Guid.NewGuid();
        acl.Add("space", spaceId, ResourcePrincipalType.Role, RoleId, ResourcePermissionLevel.Manage);

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("space", spaceId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, null, null));

        var member = WorkspaceMember.Create(Guid.NewGuid(), WorkspaceId, UserId, MembershipRole.Member, DateTimeOffset.UtcNow, RoleId);
        var svc = BuildService(acl, hierarchy, member);
        var level = await svc.GetEffectiveAsync(WorkspaceId, UserId, "space", spaceId);

        level.ShouldBe(PermissionLevel.Manage);
    }

    [Fact]
    public async Task Grant_on_an_ancestor_is_found_when_the_resource_itself_has_none_and_is_not_private()
    {
        var acl = new FakeResourcePermissionStore();
        var spaceId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        acl.Add("space", spaceId, ResourcePrincipalType.User, UserId, ResourcePermissionLevel.Edit);

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("task", taskId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, "list", listId));
        hierarchy.Set("list", listId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, "space", spaceId));
        hierarchy.Set("space", spaceId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, null, null));

        var svc = BuildService(acl, hierarchy, member: null);
        var level = await svc.GetEffectiveAsync(WorkspaceId, UserId, "task", taskId);

        level.ShouldBe(PermissionLevel.Edit);
    }

    [Fact]
    public async Task Private_ancestor_with_no_grant_hard_stops_the_walk_even_though_the_top_level_role_would_normally_allow_access()
    {
        var acl = new FakeResourcePermissionStore(); // no grants anywhere
        var spaceId = Guid.NewGuid();
        var listId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("task", taskId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, "list", listId));
        hierarchy.Set("list", listId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: true, "space", spaceId)); // private, no grant
        hierarchy.Set("space", spaceId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, null, null));

        // Member role would normally give task.edit-level access via the coarse floor — but the private
        // List ancestor must hard-stop the walk before the floor is ever consulted.
        var member = WorkspaceMember.Create(Guid.NewGuid(), WorkspaceId, UserId, MembershipRole.Member, DateTimeOffset.UtcNow);
        var svc = BuildService(acl, hierarchy, member);
        var level = await svc.GetEffectiveAsync(WorkspaceId, UserId, "task", taskId);

        level.ShouldBeNull();
    }

    [Fact]
    public async Task Private_list_inside_a_non_private_space_blocks_the_list_itself_for_a_member_with_no_grant()
    {
        var acl = new FakeResourcePermissionStore();
        var spaceId = Guid.NewGuid();
        var listId = Guid.NewGuid();

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("list", listId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: true, "space", spaceId));
        hierarchy.Set("space", spaceId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, null, null));

        var member = WorkspaceMember.Create(Guid.NewGuid(), WorkspaceId, UserId, MembershipRole.Member, DateTimeOffset.UtcNow);
        var svc = BuildService(acl, hierarchy, member);

        // The private list itself: blocked, no floor fallback.
        (await svc.GetEffectiveAsync(WorkspaceId, UserId, "list", listId)).ShouldBeNull();

        // The (non-private) space is unaffected — resolved independently, floor applies.
        (await svc.GetEffectiveAsync(WorkspaceId, UserId, "space", spaceId)).ShouldBe(PermissionLevel.Edit);
    }

    [Fact]
    public async Task Private_resource_owner_can_always_access_it_even_without_an_explicit_grant()
    {
        var acl = new FakeResourcePermissionStore(); // no ACL rows at all
        var spaceId = Guid.NewGuid();

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("space", spaceId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: true, null, null, OwnerUserId: UserId));

        var svc = BuildService(acl, hierarchy, member: null); // not even a workspace member — owner still gets in
        var level = await svc.GetEffectiveAsync(WorkspaceId, UserId, "space", spaceId);

        level.ShouldBe(PermissionLevel.Manage);
    }

    [Fact]
    public async Task No_grant_and_no_private_ancestor_falls_back_to_the_coarse_workspace_role_floor()
    {
        var acl = new FakeResourcePermissionStore();
        var spaceId = Guid.NewGuid();

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("space", spaceId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, null, null));

        var member = WorkspaceMember.Create(Guid.NewGuid(), WorkspaceId, UserId, MembershipRole.Admin, DateTimeOffset.UtcNow);
        var svc = BuildService(acl, hierarchy, member);
        var level = await svc.GetEffectiveAsync(WorkspaceId, UserId, "space", spaceId);

        level.ShouldBe(PermissionLevel.Manage); // space.manage is in the built-in Admin permission set
    }

    [Fact]
    public async Task No_grant_no_private_ancestor_and_no_member_yields_no_access()
    {
        var acl = new FakeResourcePermissionStore();
        var spaceId = Guid.NewGuid();

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("space", spaceId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, null, null));

        var svc = BuildService(acl, hierarchy, member: null);
        var level = await svc.GetEffectiveAsync(WorkspaceId, UserId, "space", spaceId);

        level.ShouldBeNull();
    }

    // ---- GetEffectiveViaAsync (multi-list task membership privacy resolution) ----

    [Fact]
    public async Task GetEffectiveViaAsync_walks_the_supplied_ancestor_instead_of_the_resource_s_natural_parent()
    {
        // The task's OWN hierarchy node points at List A (private, no grant) as its natural/primary
        // parent. GetEffectiveViaAsync is asked to evaluate the task "via" List B instead (public, no
        // ACL rows) — it must use B, not A, so a Member with no grant anywhere still gets the coarse
        // floor via B's public chain, proving the override actually takes effect.
        var acl = new FakeResourcePermissionStore();
        var taskId = Guid.NewGuid();
        var listA = Guid.NewGuid();
        var listB = Guid.NewGuid();

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("task", taskId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, "list", listA));
        hierarchy.Set("list", listA, new ResourceHierarchyNode(WorkspaceId, IsPrivate: true, null, null)); // private, no grant
        hierarchy.Set("list", listB, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, null, null)); // public

        var member = WorkspaceMember.Create(Guid.NewGuid(), WorkspaceId, UserId, MembershipRole.Member, DateTimeOffset.UtcNow);
        var svc = BuildService(acl, hierarchy, member);

        // Via the natural parent (List A, private, no grant): blocked.
        (await svc.GetEffectiveAsync(WorkspaceId, UserId, "task", taskId)).ShouldBeNull();

        // Via List B explicitly: not blocked — falls through to the Member floor (task.edit).
        var levelViaB = await svc.GetEffectiveViaAsync(WorkspaceId, UserId, "task", taskId, "list", listB);
        levelViaB.ShouldBe(PermissionLevel.Edit);
    }

    [Fact]
    public async Task GetEffectiveViaAsync_still_honors_a_grant_on_the_overridden_ancestor()
    {
        var acl = new FakeResourcePermissionStore();
        var taskId = Guid.NewGuid();
        var listA = Guid.NewGuid();
        var listB = Guid.NewGuid();
        acl.Add("list", listB, ResourcePrincipalType.User, UserId, ResourcePermissionLevel.Comment);

        var hierarchy = new FakeHierarchyQuery();
        hierarchy.Set("task", taskId, new ResourceHierarchyNode(WorkspaceId, IsPrivate: false, "list", listA));
        hierarchy.Set("list", listA, new ResourceHierarchyNode(WorkspaceId, IsPrivate: true, null, null));
        hierarchy.Set("list", listB, new ResourceHierarchyNode(WorkspaceId, IsPrivate: true, null, null)); // private too, but has a grant

        var svc = BuildService(acl, hierarchy, member: null);
        var level = await svc.GetEffectiveViaAsync(WorkspaceId, UserId, "task", taskId, "list", listB);

        level.ShouldBe(PermissionLevel.Comment);
    }

    // ---- fakes ----

    private sealed class FakeResourcePermissionStore : IResourcePermissionStore
    {
        private readonly List<ResourcePermission> _grants = new();

        public void Add(string resourceType, Guid resourceId, ResourcePrincipalType principalType, Guid principalId, ResourcePermissionLevel level)
            => _grants.Add(ResourcePermission.Create(
                Guid.NewGuid(), WorkspaceId, resourceType, resourceId, principalType, principalId, level, UserId, DateTimeOffset.UtcNow));

        void IResourcePermissionStore.Add(ResourcePermission grant) => _grants.Add(grant);

        public void Remove(ResourcePermission grant) => _grants.Remove(grant);

        public Task<ResourcePermission?> FindAsync(
            Guid workspaceId, string resourceType, Guid resourceId, ResourcePrincipalType principalType, Guid principalId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_grants.FirstOrDefault(g =>
                g.ResourceType == resourceType && g.ResourceId == resourceId
                && g.PrincipalType == principalType && g.PrincipalId == principalId));

        public Task<IReadOnlyList<ResourcePermission>> ListForResourceAsync(
            Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ResourcePermission>>(
                _grants.Where(g => g.ResourceType == resourceType && g.ResourceId == resourceId).ToList());

        public Task<bool> AnyForResourceAsync(Guid workspaceId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
            => Task.FromResult(_grants.Any(g => g.ResourceType == resourceType && g.ResourceId == resourceId));
    }

    private sealed class FakeHierarchyQuery : IResourceHierarchyQuery
    {
        private readonly Dictionary<(string, Guid), ResourceHierarchyNode> _nodes = new();

        public void Set(string resourceType, Guid resourceId, ResourceHierarchyNode node) => _nodes[(resourceType, resourceId)] = node;

        public Task<ResourceHierarchyNode?> GetAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
            => Task.FromResult(_nodes.TryGetValue((resourceType, resourceId), out var node) ? node : null);
    }

    private sealed class FakeMembershipStore(WorkspaceMember? member) : IMembershipStore
    {
        public void Add(WorkspaceMember member2) => throw new NotSupportedException();
        public void Remove(WorkspaceMember member2) => throw new NotSupportedException();
        public Task<WorkspaceMember?> FindAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(member);
        public Task<WorkspaceMember?> FindByIdAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkspaceMember>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountActiveOwnersAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListWorkspaceIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeTeamStore(IReadOnlyList<Guid> teamIds) : ITeamStore
    {
        public void Add(Team team) => throw new NotSupportedException();
        public void Remove(Team team) => throw new NotSupportedException();
        public void AddMember(TeamMembership membership) => throw new NotSupportedException();
        public void RemoveMember(TeamMembership membership) => throw new NotSupportedException();
        public Task<Team?> FindAsync(Guid teamId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Team>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TeamMembership>> ListMembersAsync(Guid teamId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TeamMembership?> FindMemberAsync(Guid teamId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, int>> CountMembersByTeamAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListTeamIdsForUserAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(teamIds);
    }

    private sealed class ThrowingRoleStore : IRoleStore
    {
        public void Add(Role role) => throw new NotSupportedException();
        public void AddPermission(RolePermission permission) => throw new NotSupportedException();
        public Task<Role?> FindByIdAsync(Guid workspaceId, Guid roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Role?> FindByKeyAsync(Guid workspaceId, string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Role>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetPermissionKeysAsync(Guid roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RoleWithPermissions>> ListWithPermissionsAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeIdGenerator : Planvexa.BuildingBlocks.Abstractions.IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class FakeClock : Planvexa.BuildingBlocks.Abstractions.IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class FakeAuditWriter : Planvexa.BuildingBlocks.Abstractions.IAuditWriter
    {
        public void Write(string action, string entityType, Guid? entityId = null, object? data = null, string? ipAddress = null)
        {
        }
    }
}
