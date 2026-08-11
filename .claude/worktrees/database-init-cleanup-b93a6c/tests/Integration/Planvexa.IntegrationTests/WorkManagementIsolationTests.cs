namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

[Collection("api")]
public sealed class WorkManagementIsolationTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Tasks_are_isolated_between_workspaces()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await clientA.CreateSpaceAsync();
        var listA = await clientA.CreateListAsync(spaceA.Id);
        var taskA = await clientA.CreateTaskAsync(listA.Id, "Secret A");

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();

        // Workspace B cannot read workspace A's task (query filter => not found).
        var read = await clientB.GetAsync(new Uri($"/api/v1/tasks/{taskA.Id}", UriKind.Relative));
        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Workspace B cannot list workspace A's list.
        var list = await clientB.GetAsync(new Uri($"/api/v1/lists/{listA.Id}/tasks", UriKind.Relative));
        list.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Row_level_security_scopes_tasks_via_a_non_superuser_role()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await clientA.CreateSpaceAsync();
        var listA = await clientA.CreateListAsync(spaceA.Id);
        var taskA = await clientA.CreateTaskAsync(listA.Id, "RLS A");

        var (clientB, workspaceB, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceB = await clientB.CreateSpaceAsync();
        var listB = await clientB.CreateListAsync(spaceB.Id);
        var taskB = await clientB.CreateTaskAsync(listB.Id, "RLS B");

        // Read workspace B's tasks directly through the non-superuser role with B's workspace set.
        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await SetWorkspaceGucAsync(connection, workspaceB);

        var visibleTitles = await ReadTaskTitlesAsync(connection);
        visibleTitles.ShouldContain("RLS B");
        visibleTitles.ShouldNotContain("RLS A");
        _ = taskA;
        _ = taskB;
    }

    [Fact]
    public async Task Guest_cannot_modify_tasks_but_can_read()
    {
        // Owner sets up a workspace with a task.
        var (ownerClient, workspaceId, slug, ownerSubject) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync();
        var list = await ownerClient.CreateListAsync(space.Id);
        var task = await ownerClient.CreateTaskAsync(list.Id, "Owned");

        // Invite a Guest and accept.
        var inviteResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/invitations", new { email = "guest@planvexa.test", role = "Guest" });
        inviteResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var token = fixture.LastInvitationToken("guest@planvexa.test");

        var guestSubject = TestData.NewSubject();
        await fixture.AuthClient(guestSubject).PostAsync(
            new Uri($"/api/v1/invitations/{token}/accept", UriKind.Relative), null);

        var guestClient = fixture.WorkClient(guestSubject, slug, workspaceId);

        // Guest can read the task.
        (await guestClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Guest cannot create a task.
        var create = await guestClient.PostAsJsonAsync("/api/v1/tasks", new { listId = list.Id, title = "Nope" });
        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Guest cannot create a space (structure management).
        var createSpace = await guestClient.PostAsJsonAsync("/api/v1/spaces", new { name = "Nope" });
        createSpace.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        _ = ownerSubject;
    }

    [Fact]
    public async Task Creating_a_space_without_a_workspace_header_is_rejected()
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("nw");
        await fixture.AuthClient(subject).RegisterOrgAsync(slug);

        // Authenticated, but no X-Workspace header.
        var client = fixture.AuthClient(subject);
        var response = await client.PostAsJsonAsync("/api/v1/spaces", new { name = "Orphan" });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Workspace_owned_child_rows_are_stamped_with_workspace_id()
    {
        // Creating a list provisions a default status scheme + statuses (an IWorkspaceOwned child).
        // Those rows are stamped with the workspace id by EnforceWorkspaceIsolation.
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        await client.CreateListAsync(space.Id);

        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        var stamped = await CountAsync(connection,
            "SELECT count(*) FROM work.statuses s JOIN work.status_schemes c ON s.scheme_id = c.id WHERE c.workspace_id = @ws AND s.workspace_id = @ws",
            workspaceId);
        var mismatched = await CountAsync(connection,
            "SELECT count(*) FROM work.statuses s JOIN work.status_schemes c ON s.scheme_id = c.id WHERE c.workspace_id = @ws AND (s.workspace_id IS NULL OR s.workspace_id <> @ws)",
            workspaceId);

        stamped.ShouldBeGreaterThan(0L);
        mismatched.ShouldBe(0L);
    }

    private static async Task<long> CountAsync(Npgsql.NpgsqlConnection connection, string sql, Guid workspaceId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("ws", workspaceId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Workspace_rls_isolates_rows_between_independent_workspaces()
    {
        // Workspace is the sole isolation boundary now (AGENTS.md; no more Tenant layer). Seed two
        // independent workspaces' rows directly and prove workspace_isolation RLS scopes reads to the
        // ambient workspace only.
        var workspaceA = Guid.CreateVersion7();
        var workspaceB = Guid.CreateVersion7();
        var schemeA = Guid.CreateVersion7();
        var schemeB = Guid.CreateVersion7();

        await using (var superuser = new Npgsql.NpgsqlConnection(fixture.ConnectionString))
        {
            await superuser.OpenAsync();
            await using var seed = superuser.CreateCommand();
            seed.CommandText =
                $"INSERT INTO work.status_schemes (id, workspace_id, name, is_default) VALUES " +
                $"('{schemeA}', '{workspaceA}', 'Scheme A', true), " +
                $"('{schemeB}', '{workspaceB}', 'Scheme B', true);";
            await seed.ExecuteNonQueryAsync();
        }

        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await SetWorkspaceGucAsync(connection, workspaceA);

        var names = new List<string>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT name FROM work.status_schemes";
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }
        }

        names.ShouldContain("Scheme A");
        names.ShouldNotContain("Scheme B");
    }

    private static async Task SetWorkspaceGucAsync(Npgsql.NpgsqlConnection connection, Guid workspaceId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_workspace', @workspace, false)";
        command.Parameters.AddWithValue("workspace", workspaceId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ReadTaskTitlesAsync(Npgsql.NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT title FROM work.tasks";
        var titles = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            titles.Add(reader.GetString(0));
        }

        return titles;
    }
}
