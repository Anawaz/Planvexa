namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Planvexa.Modules.Collaboration.Application;
using Shouldly;
using Xunit;

internal sealed record CommentAttachmentResp(
    Guid Id, Guid CommentId, string FileName, string ContentType, long SizeBytes,
    Guid UploadedByUserId, DateTimeOffset CreatedAtUtc);

[Collection("api")]
public sealed class CommentAttachmentFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Upload_appears_on_the_comment_and_download_delete_round_trip_the_bytes()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Discuss with a file");

        var comment = await (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "See attached" }))
            .Content.ReadFromJsonAsync<CommentResp>();

        var bytes = "hello comment"u8.ToArray();
        var upload = await client.PostAsync(
            new Uri($"/api/v1/comments/{comment!.Id}/attachments", UriKind.Relative),
            FileContent(bytes, "notes.txt", "text/plain"));

        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var attachment = await upload.Content.ReadFromJsonAsync<CommentAttachmentResp>();
        attachment!.FileName.ShouldBe("notes.txt");
        attachment.CommentId.ShouldBe(comment.Id);
        attachment.SizeBytes.ShouldBe(bytes.Length);

        // The attachment rides along on the comment DTO, no separate list call needed.
        var threads = await client.GetFromJsonAsync<List<CommentResp>>($"/api/v1/tasks/{task.Id}/comments");
        var loaded = threads!.Single(c => c.Id == comment.Id);
        loaded.Attachments.Single().Id.ShouldBe(attachment.Id);

        var download = await client.GetAsync(new Uri($"/api/v1/comment-attachments/{attachment.Id}/download", UriKind.Relative));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
        download.Content.Headers.ContentDisposition?.DispositionType.ShouldBe("attachment");

        var delete = await client.DeleteAsync(new Uri($"/api/v1/comment-attachments/{attachment.Id}", UriKind.Relative));
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDelete = await client.GetFromJsonAsync<List<CommentResp>>($"/api/v1/tasks/{task.Id}/comments");
        afterDelete!.Single(c => c.Id == comment.Id).Attachments.ShouldBeEmpty();
        (await client.GetAsync(new Uri($"/api/v1/comment-attachments/{attachment.Id}/download", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Attachments_are_invisible_to_another_workspace()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await clientA.CreateSpaceAsync();
        var list = await clientA.CreateListAsync(space.Id);
        var task = await clientA.CreateTaskAsync(list.Id, "Workspace A task");
        var comment = await (await clientA.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "hi" }))
            .Content.ReadFromJsonAsync<CommentResp>();

        var upload = await clientA.PostAsync(
            new Uri($"/api/v1/comments/{comment!.Id}/attachments", UriKind.Relative),
            FileContent("secret"u8.ToArray(), "secret.txt", "text/plain"));
        upload.EnsureSuccessStatusCode();
        var attachment = await upload.Content.ReadFromJsonAsync<CommentAttachmentResp>();

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();

        (await clientB.GetAsync(new Uri($"/api/v1/comment-attachments/{attachment!.Id}/download", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await clientB.DeleteAsync(new Uri($"/api/v1/comment-attachments/{attachment.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await clientB.PostAsync(
                new Uri($"/api/v1/comments/{comment.Id}/attachments", UriKind.Relative),
                FileContent("nope"u8.ToArray(), "nope.txt", "text/plain")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_the_uploader_or_an_admin_can_delete_another_members_attachment()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Shared task");
        var comment = await (await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "hi" }))
            .Content.ReadFromJsonAsync<CommentResp>();

        var upload = await owner.PostAsync(
            new Uri($"/api/v1/comments/{comment!.Id}/attachments", UriKind.Relative),
            FileContent("owner file"u8.ToArray(), "owner.txt", "text/plain"));
        var attachment = (await upload.Content.ReadFromJsonAsync<CommentAttachmentResp>())!;

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "cmt-att");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        // A plain member can read/download but not delete another member's attachment.
        (await memberClient.GetAsync(new Uri($"/api/v1/comment-attachments/{attachment.Id}/download", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.DeleteAsync(new Uri($"/api/v1/comment-attachments/{attachment.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The workspace owner (Admin+) can.
        (await owner.DeleteAsync(new Uri($"/api/v1/comment-attachments/{attachment.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_member_without_a_grant_on_a_private_task_cannot_list_comments_or_download_its_attachments()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Private task");

        var comment = await (await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "secret note" }))
            .Content.ReadFromJsonAsync<CommentResp>();
        var upload = await owner.PostAsync(
            new Uri($"/api/v1/comments/{comment!.Id}/attachments", UriKind.Relative),
            FileContent("secret"u8.ToArray(), "secret.txt", "text/plain"));
        var attachment = (await upload.Content.ReadFromJsonAsync<CommentAttachmentResp>())!;

        (await owner.PatchAsJsonAsync($"/api/v1/resources/task/{task.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "cmt-att-priv");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        // Plain workspace membership alone must not surface comments/attachments on a private task.
        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}/comments", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await memberClient.GetAsync(new Uri($"/api/v1/comment-attachments/{attachment.Id}/download", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Granting the member view access on the task restores both.
        (await owner.PostAsJsonAsync(
                $"/api/v1/resources/task/{task.Id}/permissions",
                new { principalType = "user", principalId = memberUserId, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}/comments", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.GetAsync(new Uri($"/api/v1/comment-attachments/{attachment.Id}/download", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The task owner always has full access to their own private task.
        (await owner.GetAsync(new Uri($"/api/v1/tasks/{task.Id}/comments", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_member_without_a_grant_on_a_private_task_cannot_write_to_its_comment_thread()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Private task for writes");

        var comment = await (await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "seed" }))
            .Content.ReadFromJsonAsync<CommentResp>();

        (await owner.PatchAsJsonAsync($"/api/v1/resources/task/{task.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "cmt-write-priv");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        // Plain workspace membership alone must not let the member write into the private task's
        // comment thread — post/edit/react/upload/delete must all 403, matching the read-path fix.
        (await memberClient.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "sneaky" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await memberClient.PatchAsJsonAsync($"/api/v1/comments/{comment!.Id}", new { body = "sneaky edit" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await memberClient.PostAsJsonAsync($"/api/v1/comments/{comment.Id}/reactions", new { emoji = "👀" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await memberClient.PostAsync(
                new Uri($"/api/v1/comments/{comment.Id}/attachments", UriKind.Relative),
                FileContent("nope"u8.ToArray(), "nope.txt", "text/plain")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await memberClient.DeleteAsync(new Uri($"/api/v1/comments/{comment.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Granting the member view access on the task restores write access too.
        (await owner.PostAsJsonAsync(
                $"/api/v1/resources/task/{task.Id}/permissions",
                new { principalType = "user", principalId = memberUserId, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var memberComment = await (await memberClient.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "now allowed" }))
            .Content.ReadFromJsonAsync<CommentResp>();
        memberComment.ShouldNotBeNull();
        (await memberClient.PostAsJsonAsync($"/api/v1/comments/{comment.Id}/reactions", new { emoji = "👀" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.PostAsync(
                new Uri($"/api/v1/comments/{comment.Id}/attachments", UriKind.Relative),
                FileContent("ok now"u8.ToArray(), "ok.txt", "text/plain")))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // The task owner always has full access to their own private task's comment thread.
        (await owner.PatchAsJsonAsync($"/api/v1/comments/{comment.Id}", new { body = "owner edit" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.DeleteAsync(new Uri($"/api/v1/comments/{comment.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Oversized_upload_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Too big");
        var comment = await (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/comments", new { body = "hi" }))
            .Content.ReadFromJsonAsync<CommentResp>();

        var oversize = new byte[CommentAttachmentService.MaxAttachmentBytes + 1];
        var response = await client.PostAsync(
            new Uri($"/api/v1/comments/{comment!.Id}/attachments", UriKind.Relative),
            FileContent(oversize, "huge.bin", "application/octet-stream"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }
}
