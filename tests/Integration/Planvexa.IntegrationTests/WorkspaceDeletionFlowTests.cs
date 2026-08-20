namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

/// <summary>
/// Owner-only, irreversible workspace hard-delete (POST /workspaces/{id}/delete). The single DELETE
/// relies on the workspace_id foreign-key cascade added in 0092, so these tests assert the child rows
/// are actually gone from the database rather than merely invisible through the API — and that a
/// second workspace, the audit trail, and other users' data are untouched.
/// </summary>
[Collection("api")]
public sealed class WorkspaceDeletionFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Owner_deletes_workspace_and_every_child_row_and_file_goes_with_it()
    {
        var (client, workspaceId, slug, subject) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Doomed with the workspace");

        var upload = await client.PostAsync(
            new Uri($"/api/v1/tasks/{task.Id}/attachments", UriKind.Relative),
            FileContent("bytes"u8.ToArray(), "notes.txt", "text/plain"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The second workspace of the SAME user: nothing here may be touched by the delete below.
        var survivorSlug = TestData.NewSlug("survivor");
        var (created, survivor) = await fixture.AuthClient(subject).RegisterOrgAsync(survivorSlug);
        created.EnsureSuccessStatusCode();
        var survivorClient = fixture.WorkClient(subject, survivor.Id);
        var survivorSpace = await survivorClient.CreateSpaceAsync();
        var survivorList = await survivorClient.CreateListAsync(survivorSpace.Id);
        var survivorTask = await survivorClient.CreateTaskAsync(survivorList.Id, "Untouched");

        var workspaceFiles = Path.Combine(StorageRoot(), "workspaces", workspaceId.ToString());
        Directory.Exists(workspaceFiles).ShouldBeTrue();

        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/delete", new { confirmSlug = slug });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CountAsync("SELECT count(*) FROM tenancy.workspaces WHERE id = @id", workspaceId)).ShouldBe(0L);
        (await CountAsync("SELECT count(*) FROM tenancy.workspace_members WHERE workspace_id = @id", workspaceId)).ShouldBe(0L);
        (await CountAsync("SELECT count(*) FROM work.spaces WHERE id = @id", space.Id)).ShouldBe(0L);
        (await CountAsync("SELECT count(*) FROM work.lists WHERE id = @id", list.Id)).ShouldBe(0L);
        (await CountAsync("SELECT count(*) FROM work.tasks WHERE id = @id", task.Id)).ShouldBe(0L);
        (await CountAsync("SELECT count(*) FROM work.task_attachments WHERE workspace_id = @id", workspaceId)).ShouldBe(0L);
        // Cleared explicitly by the service: outbox rows are deliberately outside the cascade.
        (await CountAsync("SELECT count(*) FROM platform.outbox_messages WHERE workspace_id = @id", workspaceId)).ShouldBe(0L);

        // Blobs live outside the transaction and are swept by prefix.
        Directory.Exists(workspaceFiles).ShouldBeFalse();

        // Audit history OUTLIVES the workspace it describes — audit.audit_events is excluded from 0092.
        (await CountAsync(
            "SELECT count(*) FROM audit.audit_events WHERE action = 'workspace.deleted' AND entity_id = @id", workspaceId))
            .ShouldBe(1L);

        // Isolation: the user's other workspace is completely intact.
        (await CountAsync("SELECT count(*) FROM tenancy.workspaces WHERE id = @id", survivor.Id)).ShouldBe(1L);
        (await CountAsync("SELECT count(*) FROM work.spaces WHERE id = @id", survivorSpace.Id)).ShouldBe(1L);
        (await CountAsync("SELECT count(*) FROM work.lists WHERE id = @id", survivorList.Id)).ShouldBe(1L);
        (await CountAsync("SELECT count(*) FROM work.tasks WHERE id = @id", survivorTask.Id)).ShouldBe(1L);
        (await survivorClient.GetAsync(new Uri($"/api/v1/tasks/{survivorTask.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Non_owner_member_cannot_delete_the_workspace()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "wsdel-member");
        var member = fixture.WorkClient(memberSubject, workspaceId);

        (await member.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/delete", new { confirmSlug = slug }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await CountAsync("SELECT count(*) FROM tenancy.workspaces WHERE id = @id", workspaceId)).ShouldBe(1L);
    }

    [Fact]
    public async Task Confirmation_slug_must_match_exactly()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();

        (await owner.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/delete", new { confirmSlug = $"{slug}-nope" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Empty/absent is caught earlier, by the request validator.
        (await owner.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/delete", new { confirmSlug = "" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await CountAsync("SELECT count(*) FROM tenancy.workspaces WHERE id = @id", workspaceId)).ShouldBe(1L);
    }

    [Fact]
    public async Task An_owner_of_another_workspace_cannot_delete_this_one()
    {
        var (_, victimId, victimSlug, _) = await fixture.NewWorkspaceClientAsync();
        var (attacker, _, _, attackerSubject) = await fixture.NewWorkspaceClientAsync();

        // Owner of their OWN workspace, aiming at somebody else's id.
        (await attacker.PostAsJsonAsync($"/api/v1/workspaces/{victimId}/delete", new { confirmSlug = victimSlug }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And with the victim's workspace as the ambient one, they are not a member at all.
        var stranger = fixture.WorkClient(attackerSubject, victimId);
        (await stranger.PostAsJsonAsync($"/api/v1/workspaces/{victimId}/delete", new { confirmSlug = victimSlug }))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        (await CountAsync("SELECT count(*) FROM tenancy.workspaces WHERE id = @id", victimId)).ShouldBe(1L);
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }

    private string StorageRoot() =>
        fixture.Factory.Services.GetRequiredService<IConfiguration>()["FileStorage:RootPath"]!;

    /// <summary>Superuser connection: these assertions are about what physically remains in the
    /// database, so they must not be filtered by RLS.</summary>
    private async Task<long> CountAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
