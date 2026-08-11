namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Planvexa.Modules.WorkManagement.Application.Services;
using Shouldly;
using Xunit;

internal sealed record AttachmentResp(
    Guid Id, Guid TaskId, string FileName, string ContentType, long SizeBytes,
    Guid UploadedByUserId, DateTimeOffset CreatedAtUtc);

[Collection("api")]
public sealed class AttachmentFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Upload_list_download_and_delete_round_trips_the_bytes()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "With attachment");

        var bytes = "hello planvexa"u8.ToArray();
        var upload = await client.PostAsync(
            new Uri($"/api/v1/tasks/{task.Id}/attachments", UriKind.Relative),
            FileContent(bytes, "notes.txt", "text/plain"));

        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentResp>();
        attachment!.FileName.ShouldBe("notes.txt");
        attachment.SizeBytes.ShouldBe(bytes.Length);

        var listed = await client.GetFromJsonAsync<List<AttachmentResp>>($"/api/v1/tasks/{task.Id}/attachments");
        listed!.Single().Id.ShouldBe(attachment.Id);

        var download = await client.GetAsync(new Uri($"/api/v1/attachments/{attachment.Id}/download", UriKind.Relative));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
        download.Content.Headers.ContentDisposition?.DispositionType.ShouldBe("attachment");

        var delete = await client.DeleteAsync(new Uri($"/api/v1/attachments/{attachment.Id}", UriKind.Relative));
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<List<AttachmentResp>>($"/api/v1/tasks/{task.Id}/attachments"))!.ShouldBeEmpty();
        (await client.GetAsync(new Uri($"/api/v1/attachments/{attachment.Id}/download", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Attachments_are_invisible_to_another_tenant()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await clientA.CreateSpaceAsync();
        var list = await clientA.CreateListAsync(space.Id);
        var task = await clientA.CreateTaskAsync(list.Id, "Tenant A file");

        var upload = await clientA.PostAsync(
            new Uri($"/api/v1/tasks/{task.Id}/attachments", UriKind.Relative),
            FileContent("secret"u8.ToArray(), "secret.txt", "text/plain"));
        upload.EnsureSuccessStatusCode();
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentResp>();

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();

        (await clientB.GetAsync(new Uri($"/api/v1/attachments/{attachment!.Id}/download", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await clientB.DeleteAsync(new Uri($"/api/v1/attachments/{attachment.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await clientB.GetAsync(new Uri($"/api/v1/tasks/{task.Id}/attachments", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Guest_cannot_upload_but_can_list()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Guest read");

        var (guestSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "att-g", role: "Guest");
        var guest = fixture.WorkClient(guestSubject, slug, workspaceId);

        var upload = await guest.PostAsync(
            new Uri($"/api/v1/tasks/{task.Id}/attachments", UriKind.Relative),
            FileContent("nope"u8.ToArray(), "nope.txt", "text/plain"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await guest.GetAsync(new Uri($"/api/v1/tasks/{task.Id}/attachments", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Oversized_upload_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Too big");

        var oversize = new byte[AttachmentService.MaxAttachmentBytes + 1];
        var response = await client.PostAsync(
            new Uri($"/api/v1/tasks/{task.Id}/attachments", UriKind.Relative),
            FileContent(oversize, "huge.bin", "application/octet-stream"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Empty_upload_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Empty");

        var response = await client.PostAsync(
            new Uri($"/api/v1/tasks/{task.Id}/attachments", UriKind.Relative),
            FileContent([], "empty.txt", "text/plain"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Traversal_in_the_file_name_is_stripped()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Traversal");

        var upload = await client.PostAsync(
            new Uri($"/api/v1/tasks/{task.Id}/attachments", UriKind.Relative),
            FileContent("x"u8.ToArray(), "../../../etc/passwd", "text/plain"));

        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await upload.Content.ReadFromJsonAsync<AttachmentResp>())!.FileName.ShouldBe("passwd");
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }
}
