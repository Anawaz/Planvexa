namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

[Collection("api")]
public sealed class TimeTrackingIsolationTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Time_entries_are_isolated_between_tenants()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await clientA.CreateSpaceAsync();
        var listA = await clientA.CreateListAsync(spaceA.Id);
        var taskA = await clientA.CreateTaskAsync(listA.Id, "A");
        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");
        await clientA.PostAsJsonAsync("/api/v1/time-entries", new { taskId = taskA.Id, startedAtUtc = start, endedAtUtc = start.AddHours(1), timeZoneId = "UTC" });

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();

        // Tenant B sees none of tenant A's entries.
        var from = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-03-08T00:00:00Z");
        var entries = await clientB.GetFromJsonAsync<List<TimeEntryResp>>(
            $"/api/v1/time-entries?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");
        entries!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Row_level_security_scopes_time_entries_via_non_superuser_role()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");
        await clientA.PostAsJsonAsync("/api/v1/time-entries", new { startedAtUtc = start, endedAtUtc = start.AddHours(1), description = "RLS-A-time", timeZoneId = "UTC" });

        var (clientB, workspaceB, _, _) = await fixture.NewWorkspaceClientAsync();
        await clientB.PostAsJsonAsync("/api/v1/time-entries", new { startedAtUtc = start, endedAtUtc = start.AddHours(1), description = "RLS-B-time", timeZoneId = "UTC" });

        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await using (var set = connection.CreateCommand())
        {
            set.CommandText = "SELECT set_config('app.current_workspace', @w, false)";
            set.Parameters.AddWithValue("w", workspaceB.ToString());
            await set.ExecuteNonQueryAsync();
        }

        var descriptions = new List<string>();
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT description FROM time.time_entries WHERE description IS NOT NULL";
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            descriptions.Add(reader.GetString(0));
        }

        descriptions.ShouldContain("RLS-B-time");
        descriptions.ShouldNotContain("RLS-A-time");
    }

    [Fact]
    public async Task Member_cannot_edit_another_users_entry_or_manage_policy()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");
        var create = await owner.PostAsJsonAsync("/api/v1/time-entries", new { startedAtUtc = start, endedAtUtc = start.AddHours(1), timeZoneId = "UTC" });
        var ownerEntry = await create.Content.ReadFromJsonAsync<TimeEntryResp>();

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "tt");
        var member = fixture.WorkClient(memberSubject, workspaceId);

        // Member cannot edit the owner's entry.
        var edit = await member.PatchAsJsonAsync($"/api/v1/time-entries/{ownerEntry!.Id}", new { endedAtUtc = start.AddHours(2) });
        edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Member cannot change the workspace time policy (Admin+).
        var policy = await member.PutAsJsonAsync("/api/v1/time-policy", new
        {
            singleActiveTimer = false,
            roundingMinutes = 0,
            minimumDurationSeconds = 0,
            maximumEntrySeconds = 86400,
            billableByDefault = true,
            requireDescription = false,
            requireTask = false,
            editWindowHours = 0,
            approvalRequired = false,
            weekStartsOn = 1,
            lockDateUtc = (DateTimeOffset?)null,
            overtimeThresholdSeconds = 144000,
        });
        policy.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Member cannot approve a timesheet.
        var reports = await member.GetAsync(new Uri("/api/v1/reports/utilization?from=2026-03-01T00:00:00Z&to=2026-03-08T00:00:00Z", UriKind.Relative));
        reports.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Guest_cannot_track_time()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (guestSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "gt", role: "Guest");
        var guest = fixture.WorkClient(guestSubject, workspaceId);

        var start = await guest.PostAsJsonAsync("/api/v1/timers/start", new { description = "nope" });
        start.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
