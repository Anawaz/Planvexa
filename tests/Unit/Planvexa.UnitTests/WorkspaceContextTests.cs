using Planvexa.BuildingBlocks.Workspaces;
using Shouldly;
using Xunit;

namespace Planvexa.UnitTests;

/// <summary>
/// Covers the Workspace ownership abstractions introduced for ADR 0015: immutable
/// <see cref="WorkspaceContext"/> and its accessor. Populated by TenantResolutionMiddleware.
/// </summary>
public sealed class WorkspaceContextTests
{
    [Fact]
    public void None_has_no_workspace_and_empty_collections()
    {
        var ctx = WorkspaceContext.None;

        ctx.HasWorkspace.ShouldBeFalse();
        ctx.WorkspaceId.ShouldBe(Guid.Empty);
        ctx.Role.ShouldBe(string.Empty);
        ctx.Permissions.ShouldBeEmpty();
        ctx.Entitlements.ShouldBeEmpty();
        ctx.CorrelationId.ShouldBe(string.Empty);
    }

    [Fact]
    public void Full_constructor_populates_all_members()
    {
        var workspaceId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();

        var ctx = new WorkspaceContext(
            workspaceId,
            userId,
            membershipId,
            role: "Admin",
            permissions: new HashSet<string> { "task.create" },
            entitlements: new HashSet<string> { "feature.gantt" },
            correlationId: "corr-1");

        ctx.HasWorkspace.ShouldBeTrue();
        ctx.WorkspaceId.ShouldBe(workspaceId);
        ctx.UserId.ShouldBe(userId);
        ctx.MembershipId.ShouldBe(membershipId);
        ctx.Role.ShouldBe("Admin");
        ctx.Permissions.ShouldContain("task.create");
        ctx.Entitlements.ShouldContain("feature.gantt");
        ctx.CorrelationId.ShouldBe("corr-1");
    }

    [Fact]
    public void Accessor_defaults_to_none_and_stores_set_context()
    {
        var accessor = new WorkspaceContextAccessor();
        accessor.Current.HasWorkspace.ShouldBeFalse();

        var ctx = new WorkspaceContext(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            "Member", new HashSet<string>(), new HashSet<string>(), "c");
        accessor.Set(ctx);
        accessor.Current.ShouldBeSameAs(ctx);

        accessor.Set(null!);
        accessor.Current.ShouldBe(WorkspaceContext.None);
    }
}
