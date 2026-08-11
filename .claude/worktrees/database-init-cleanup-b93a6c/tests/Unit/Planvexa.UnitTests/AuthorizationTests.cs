namespace Planvexa.UnitTests.Tenancy;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.Modules.Tenancy.Domain;
using Shouldly;
using Xunit;

public sealed class AuthorizationTests
{
    [Theory]
    [InlineData(MembershipRole.Owner, TenancyPermissions.WorkspaceManage, true)]
    [InlineData(MembershipRole.Admin, TenancyPermissions.WorkspaceManage, true)]
    [InlineData(MembershipRole.Member, TenancyPermissions.MembersView, true)]
    [InlineData(MembershipRole.Member, TenancyPermissions.MembersInvite, false)]
    [InlineData(MembershipRole.Guest, TenancyPermissions.MembersView, false)]
    [InlineData(MembershipRole.LimitedMember, TenancyPermissions.TaskView, true)]
    [InlineData(MembershipRole.LimitedMember, TenancyPermissions.TaskComment, true)]
    [InlineData(MembershipRole.LimitedMember, TenancyPermissions.SpaceView, false)]
    [InlineData(MembershipRole.LimitedMember, TenancyPermissions.MembersView, false)]
    [InlineData(MembershipRole.Guest, TenancyPermissions.TaskView, true)]
    [InlineData(MembershipRole.Guest, TenancyPermissions.TaskComment, false)]
    public void Role_permissions_are_enforced(MembershipRole role, string permission, bool expected)
        => TenancyAuthorizer.Can(role, permission).ShouldBe(expected);

    [Fact]
    public void Null_role_is_never_authorized()
        => TenancyAuthorizer.Can((MembershipRole?)null, TenancyPermissions.MembersView).ShouldBeFalse();

    [Fact]
    public void Ensure_throws_forbidden_when_not_permitted()
        => Should.Throw<ForbiddenException>(
            () => TenancyAuthorizer.Ensure(MembershipRole.Guest, TenancyPermissions.WorkspaceManage));

    [Fact]
    public void Ensure_passes_when_permitted()
        => Should.NotThrow(() => TenancyAuthorizer.Ensure(MembershipRole.Owner, TenancyPermissions.RolesManage));

    [Fact]
    public void Limited_member_is_a_strict_subset_of_member()
    {
        var memberPermissions = BuiltInRoles.For(MembershipRole.Member).Permissions;
        var limitedMemberPermissions = BuiltInRoles.For(MembershipRole.LimitedMember).Permissions;

        limitedMemberPermissions.ShouldNotBeEmpty();
        limitedMemberPermissions.ShouldBeSubsetOf(memberPermissions);
        limitedMemberPermissions.Count.ShouldBeLessThan(memberPermissions.Count);
    }

    [Fact]
    public void Owner_grants_every_built_in_permission_key()
    {
        var allKeys = BuiltInRoles.All.SelectMany(d => d.Permissions).Distinct();
        var ownerPermissions = BuiltInRoles.For(MembershipRole.Owner).Permissions;
        foreach (var key in allKeys)
        {
            ownerPermissions.ShouldContain(key);
        }
    }

    [Fact]
    public void Admin_lacks_roles_manage_but_owner_has_it()
    {
        BuiltInRoles.For(MembershipRole.Admin).Permissions.ShouldNotContain(TenancyPermissions.RolesManage);
        BuiltInRoles.For(MembershipRole.Owner).Permissions.ShouldContain(TenancyPermissions.RolesManage);
    }

    [Theory]
    [InlineData(TenancyPermissions.MembersView, true)]
    [InlineData(TenancyPermissions.WorkspaceManage, false)]
    public void Db_backed_overload_checks_the_resolved_permission_set(string permission, bool expected)
    {
        var resolved = new HashSet<string> { TenancyPermissions.MembersView, TenancyPermissions.TaskView };
        TenancyAuthorizer.Can(resolved, permission).ShouldBe(expected);
    }

    [Fact]
    public void Db_backed_ensure_throws_forbidden_when_not_in_the_resolved_set()
        => Should.Throw<ForbiddenException>(
            () => TenancyAuthorizer.Ensure(RolePermissions.None, TenancyPermissions.WorkspaceManage));
}
