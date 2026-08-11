namespace Planvexa.UnitTests.Reporting;

using Planvexa.Modules.Reporting.Application.Services;
using Planvexa.Modules.Reporting.Authorization;
using Planvexa.Modules.Reporting.Domain;
using Planvexa.SharedContracts.Workspaces;
using Shouldly;
using Xunit;

public sealed class DashboardDomainTests
{
    [Fact]
    public void Private_dashboard_is_visible_only_to_owner()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var dashboard = Dashboard.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Ops", isPrivate: true, owner, DateTimeOffset.UtcNow);

        dashboard.CanBeViewedBy(owner).ShouldBeTrue();
        dashboard.CanBeViewedBy(other).ShouldBeFalse();
        Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() => dashboard.EnsureViewableBy(other));
    }

    [Fact]
    public void Shared_dashboard_is_visible_to_others()
    {
        var owner = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var dashboard = Dashboard.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Team", isPrivate: false, owner, DateTimeOffset.UtcNow);

        dashboard.CanBeViewedBy(other).ShouldBeTrue();
    }

    [Fact]
    public void ReplaceWidgets_replaces_the_full_set_in_order()
    {
        var dashboard = Dashboard.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "D", isPrivate: false, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        dashboard.AddWidget(Guid.CreateVersion7(), WidgetType.Overdue, "{}", 0);

        dashboard.ReplaceWidgets(
            new[]
            {
                (Guid.CreateVersion7(), WidgetType.TasksByStatus, "{}", 0),
                (Guid.CreateVersion7(), WidgetType.Completed, "{}", 1),
            },
            DateTimeOffset.UtcNow);

        dashboard.Widgets.Count.ShouldBe(2);
        dashboard.Widgets.Select(w => w.Type).ShouldBe(new[] { WidgetType.TasksByStatus, WidgetType.Completed });
    }
}

public sealed class HealthPercentTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 5, 50)]
    [InlineData(4, 3, 75)]
    [InlineData(3, 1, 33.3)]
    public void HealthPercent_is_completed_over_total(int total, int completed, decimal expected)
        => WidgetComputer.HealthPercent(total, completed).ShouldBe(expected);
}

public sealed class ReportingAuthorizerTests
{
    [Theory]
    [InlineData(WorkspaceRole.Guest, false)]
    [InlineData(WorkspaceRole.Member, true)]
    [InlineData(WorkspaceRole.Admin, true)]
    public void Edit_requires_member(WorkspaceRole role, bool allowed)
        => ReportingAuthorizer.CanEdit(role).ShouldBe(allowed);

    [Fact]
    public void EnsureManage_throws_for_member()
        => Should.Throw<Planvexa.BuildingBlocks.Exceptions.ForbiddenException>(() =>
            ReportingAuthorizer.EnsureManage(WorkspaceRole.Member));
}
