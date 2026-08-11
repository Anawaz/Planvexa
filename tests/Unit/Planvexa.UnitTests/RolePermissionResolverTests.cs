namespace Planvexa.UnitTests.Tenancy;

using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.Modules.Tenancy.Domain;
using Shouldly;
using Xunit;

public sealed class RolePermissionResolverTests
{
    [Fact]
    public async Task Null_member_resolves_to_no_permissions()
    {
        var resolver = new RolePermissionResolver(new FakeRoleStore());
        var result = await resolver.ResolveAsync(null);
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Member_with_no_role_id_falls_back_to_the_enum_fast_path_without_a_db_call()
    {
        var store = new FakeRoleStore();
        var resolver = new RolePermissionResolver(store);
        var member = WorkspaceMember.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MembershipRole.Admin, DateTimeOffset.UtcNow);

        var result = await resolver.ResolveAsync(member);

        result.ShouldBe(BuiltInRoles.For(MembershipRole.Admin).Permissions, ignoreOrder: true);
        store.GetPermissionKeysCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Member_with_a_role_id_resolves_from_the_store_and_caches_the_result()
    {
        var roleId = Guid.NewGuid();
        var store = new FakeRoleStore();
        store.Grants[roleId] = new HashSet<string> { TenancyPermissions.TaskView, TenancyPermissions.TaskEdit };
        var resolver = new RolePermissionResolver(store);
        var member = WorkspaceMember.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MembershipRole.Member, DateTimeOffset.UtcNow, roleId);

        var first = await resolver.ResolveAsync(member);
        var second = await resolver.ResolveAsync(member);

        first.ShouldBe(store.Grants[roleId], ignoreOrder: true);
        second.ShouldBe(store.Grants[roleId], ignoreOrder: true);

        // The cache key is the role id (a fresh Guid each test run), so a hit here proves caching, not
        // cross-test pollution from the process-local static cache other tests also populate.
        store.GetPermissionKeysCallCount.ShouldBe(1);
    }

    private sealed class FakeRoleStore : IRoleStore
    {
        public Dictionary<Guid, IReadOnlySet<string>> Grants { get; } = new();

        public int GetPermissionKeysCallCount { get; private set; }

        public void Add(Role role) => throw new NotSupportedException();

        public void AddPermission(RolePermission permission) => throw new NotSupportedException();

        public Task<Role?> FindByIdAsync(Guid workspaceId, Guid roleId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Role?> FindByKeyAsync(Guid workspaceId, string key, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlySet<string>> GetPermissionKeysAsync(Guid roleId, CancellationToken cancellationToken = default)
        {
            GetPermissionKeysCallCount++;
            return Task.FromResult(Grants.TryGetValue(roleId, out var perms) ? perms : new HashSet<string>());
        }

        public Task<IReadOnlyList<RoleWithPermissions>> ListWithPermissionsAsync(Guid workspaceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
