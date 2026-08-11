namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Planvexa.Modules.Identity.Application.Services;
using Shouldly;
using Xunit;

internal sealed record UserInfoResp(Guid UserId, string Email, string DisplayName, string? AvatarUrl);

/// <summary>
/// Self-service avatar upload (POST /api/v1/users/me/avatar) — same magic-byte-validate +
/// malware-scan + IFileStorage pipeline as AttachmentFlowTests exercises for task attachments (see
/// AvatarService's doc comment), just for the caller's own global User row instead of a workspace-owned
/// resource.
/// </summary>
[Collection("api")]
public sealed class AvatarFlowTests(PlanvexaFixture fixture)
{
    // A minimal valid 1x1 transparent PNG — passes FileContentValidator's magic-byte check for image/png.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public async Task Upload_sets_avatar_url_and_serves_the_image()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var upload = await client.PostAsync(
            new Uri("/api/v1/users/me/avatar", UriKind.Relative),
            FileContent(TinyPng, "avatar.png", "image/png"));

        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var uploaded = await upload.Content.ReadFromJsonAsync<UserInfoResp>();
        uploaded!.AvatarUrl.ShouldBe($"/users/{uploaded.UserId}/avatar");

        // GET /users/me returns the same pointer — this is what the frontend renders instead of initials.
        var me = await client.GetFromJsonAsync<UserInfoResp>("/api/v1/users/me");
        me!.AvatarUrl.ShouldBe(uploaded.AvatarUrl);

        // The bytes are actually retrievable at that pointer (malware scan + storage pipeline wired end
        // to end, same as AttachmentFlowTests' upload-then-download round trip).
        var download = await client.GetAsync(new Uri($"/api/v1{uploaded.AvatarUrl}", UriKind.Relative));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(TinyPng);
    }

    [Fact]
    public async Task Non_image_upload_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var upload = await client.PostAsync(
            new Uri("/api/v1/users/me/avatar", UriKind.Relative),
            FileContent("not an image"u8.ToArray(), "notes.txt", "text/plain"));

        upload.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Oversized_upload_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var oversize = new byte[AvatarService.MaxAvatarBytes + 1];
        var upload = await client.PostAsync(
            new Uri("/api/v1/users/me/avatar", UriKind.Relative),
            FileContent(oversize, "huge.png", "image/png"));

        upload.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Avatar_of_a_user_with_none_is_not_found()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var me = await client.GetFromJsonAsync<UserInfoResp>("/api/v1/users/me");

        (await client.GetAsync(new Uri($"/api/v1/users/{me!.UserId}/avatar", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }
}
