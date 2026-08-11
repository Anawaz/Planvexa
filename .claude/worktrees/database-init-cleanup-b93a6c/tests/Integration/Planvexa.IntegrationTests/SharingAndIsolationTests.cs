namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

[Collection("api")]
public sealed class SharingAndIsolationTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Public_link_returns_only_the_shared_task_and_404_after_revoke()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Shared task");
        var otherTask = await client.CreateTaskAsync(list.Id, "Private task");

        var shareResponse = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/share", new { expiresInDays = 7 });
        shareResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var share = await shareResponse.Content.ReadFromJsonAsync<ShareResp>();

        // Anonymous client (no auth headers) can read ONLY the shared task.
        var anon = fixture.Factory.CreateClient();
        var publicResponse = await anon.GetAsync(new Uri($"/api/v1/public/tasks/{share!.Token}", UriKind.Relative));
        publicResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var shared = await publicResponse.Content.ReadFromJsonAsync<SharedTaskResp>();
        shared!.TaskId.ShouldBe(task.Id);
        shared.Title.ShouldBe("Shared task");

        // A made-up token for the other task is not accessible.
        (await anon.GetAsync(new Uri($"/api/v1/public/tasks/{Guid.NewGuid():N}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _ = otherTask;

        // Revoke → public read now 404s.
        (await client.DeleteAsync(new Uri($"/api/v1/shares/{share.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anon.GetAsync(new Uri($"/api/v1/public/tasks/{share.Token}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Comments_are_isolated_between_workspaces()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await clientA.CreateSpaceAsync();
        var listA = await clientA.CreateListAsync(spaceA.Id);
        var taskA = await clientA.CreateTaskAsync(listA.Id, "A task");
        await clientA.PostAsJsonAsync($"/api/v1/tasks/{taskA.Id}/comments", new { body = "secret A" });

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();

        // Workspace B cannot read workspace A's task comments (task not found under B's workspace).
        (await clientB.GetAsync(new Uri($"/api/v1/tasks/{taskA.Id}/comments", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Row_level_security_scopes_comments_via_non_superuser_role()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await clientA.CreateSpaceAsync();
        var listA = await clientA.CreateListAsync(spaceA.Id);
        var taskA = await clientA.CreateTaskAsync(listA.Id, "A");
        await clientA.PostAsJsonAsync($"/api/v1/tasks/{taskA.Id}/comments", new { body = "RLS-A-comment" });

        var (clientB, workspaceB, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceB = await clientB.CreateSpaceAsync();
        var listB = await clientB.CreateListAsync(spaceB.Id);
        var taskB = await clientB.CreateTaskAsync(listB.Id, "B");
        await clientB.PostAsJsonAsync($"/api/v1/tasks/{taskB.Id}/comments", new { body = "RLS-B-comment" });

        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await SetWorkspaceGucAsync(connection, workspaceB);

        var bodies = await ReadCommentBodiesAsync(connection);
        bodies.ShouldContain("RLS-B-comment");
        bodies.ShouldNotContain("RLS-A-comment");
    }

    [Fact]
    public async Task Guest_can_read_comments_but_not_post()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Guarded");
        await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "hello" });

        var (guestSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "guest", role: "Guest");
        var guest = fixture.WorkClient(guestSubject, workspaceId);

        (await guest.GetAsync(new Uri($"/api/v1/tasks/{task.Id}/comments", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await guest.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "nope" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task SetWorkspaceGucAsync(Npgsql.NpgsqlConnection connection, Guid workspaceId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_workspace', @workspace, false)";
        command.Parameters.AddWithValue("workspace", workspaceId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> ReadCommentBodiesAsync(Npgsql.NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT body FROM collab.comments";
        var bodies = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            bodies.Add(reader.GetString(0));
        }

        return bodies;
    }
}
