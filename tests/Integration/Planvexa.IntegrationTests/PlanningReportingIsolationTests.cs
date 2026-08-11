namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

[Collection("api")]
public sealed class PlanningReportingIsolationTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Dashboards_are_isolated_between_tenants()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        await clientA.PostAsJsonAsync("/api/v1/dashboards", new { name = "A-dash", isPrivate = false, widgets = Array.Empty<object>() });

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var visible = await clientB.GetFromJsonAsync<List<DashboardSummaryResp>>("/api/v1/dashboards");
        visible!.ShouldNotContain(d => d.Name == "A-dash");
    }

    [Fact]
    public async Task Row_level_security_scopes_dashboards_via_non_superuser_role()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        await clientA.PostAsJsonAsync("/api/v1/dashboards", new { name = "RLS-A-dash", isPrivate = false, widgets = Array.Empty<object>() });

        var (clientB, workspaceB, _, _) = await fixture.NewWorkspaceClientAsync();
        await clientB.PostAsJsonAsync("/api/v1/dashboards", new { name = "RLS-B-dash", isPrivate = false, widgets = Array.Empty<object>() });

        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await using (var set = connection.CreateCommand())
        {
            set.CommandText = "SELECT set_config('app.current_workspace', @w, false)";
            set.Parameters.AddWithValue("w", workspaceB.ToString());
            await set.ExecuteNonQueryAsync();
        }

        var names = new List<string>();
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT name FROM reporting.dashboards";
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        names.ShouldContain("RLS-B-dash");
        names.ShouldNotContain("RLS-A-dash");
    }

    [Fact]
    public async Task Planning_tables_enforce_row_level_security()
    {
        // Create planning data (a sprint) in workspace B and verify RLS hides workspace A's rows.
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        await clientA.PostAsJsonAsync("/api/v1/sprints", new { name = "RLS-A-sprint", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z") });

        var (clientB, workspaceB, _, _) = await fixture.NewWorkspaceClientAsync();
        await clientB.PostAsJsonAsync("/api/v1/sprints", new { name = "RLS-B-sprint", startUtc = DateTimeOffset.Parse("2026-03-01Z"), endUtc = DateTimeOffset.Parse("2026-03-14Z") });

        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await using (var set = connection.CreateCommand())
        {
            set.CommandText = "SELECT set_config('app.current_workspace', @w, false)";
            set.Parameters.AddWithValue("w", workspaceB.ToString());
            await set.ExecuteNonQueryAsync();
        }

        var names = new List<string>();
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT name FROM planning.sprints";
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        names.ShouldContain("RLS-B-sprint");
        names.ShouldNotContain("RLS-A-sprint");
    }

    [Fact]
    public async Task Private_dashboard_is_invisible_to_a_non_owner_member()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/dashboards", new { name = "Owner-only", isPrivate = true, widgets = Array.Empty<object>() });
        create.EnsureSuccessStatusCode();
        var dashboard = (await create.Content.ReadFromJsonAsync<DashboardResp>())!;

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "dm");
        var member = fixture.WorkClient(memberSubject, workspaceId);

        // The private dashboard is not listed for another member…
        var memberList = await member.GetFromJsonAsync<List<DashboardSummaryResp>>("/api/v1/dashboards");
        memberList!.ShouldNotContain(d => d.Id == dashboard.Id);

        // …and cannot be fetched or read directly (widget-level authorization).
        var direct = await member.GetAsync(new Uri($"/api/v1/dashboards/{dashboard.Id}", UriKind.Relative));
        direct.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var data = await member.GetAsync(new Uri($"/api/v1/dashboards/{dashboard.Id}/data", UriKind.Relative));
        data.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The owner still sees it.
        var ownerList = await owner.GetFromJsonAsync<List<DashboardSummaryResp>>("/api/v1/dashboards");
        ownerList!.ShouldContain(d => d.Id == dashboard.Id);
    }

}
