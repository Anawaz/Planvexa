namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Npgsql;
using Shouldly;
using Xunit;

// Response shapes for the whiteboards/clips endpoints.
internal sealed record WhiteboardResp(Guid Id, string Name, bool IsPrivate, Guid OwnerUserId, string? LinkedResourceType, Guid? LinkedResourceId, bool IsArchived, DateTimeOffset UpdatedAtUtc);
internal sealed record WhiteboardCollabAccessResp(bool Allowed, bool CanEdit, Guid? UserId);
internal sealed record ClipResp(Guid Id, string Title, string? Description, bool IsPrivate, Guid OwnerUserId, string? LinkedResourceType, Guid? LinkedResourceId, string ContentType, long SizeBytes, double? DurationSeconds, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
internal sealed record SearchHitResp(string Type, Guid Id, string Title, string? Subtitle, Guid? ListId);
internal sealed record ClipTranscriptResp(string Status, string? Text, object? Segments, DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Whiteboards & Clips. Load-bearing tests are the privacy-inheritance ones — a
/// Whiteboard/Clip linked to a private Task/Document must be exactly as hidden as that Task/Document to an
/// ungranted Member, mirroring the sharing/planning/governance suites' exact regression-test shape (this roadmap has already
/// found five real confidentiality bugs in listing/aggregation paths that skipped this check) — plus the
/// Whiteboard collaboration-room authorization check (mirrors the identical Document test) and a
/// Clip transcript's permission-filtering in cross-module search.
/// </summary>
[Collection("api")]
public sealed class WhiteboardsAndClipsFlowTests(PlanvexaFixture fixture)
{
    // ---- Whiteboard collaboration-room authorization (mirrors DocumentsWikisFlowTests exactly) ----

    [Fact]
    public async Task Can_collaborate_denies_a_non_owner_member_on_a_private_whiteboard()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/whiteboards", new { name = "Secret board", isPrivate = true });
        var wb = (await create.Content.ReadFromJsonAsync<WhiteboardResp>())!;

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "wbcc1");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var response = await member.GetAsync(new Uri($"/api/v1/internal/whiteboards/{wb.Id}/can-collaborate", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var access = (await response.Content.ReadFromJsonAsync<WhiteboardCollabAccessResp>())!;
        access.Allowed.ShouldBeFalse();
        access.CanEdit.ShouldBeFalse();
    }

    [Fact]
    public async Task Can_collaborate_allows_the_owner_and_a_shared_workspace_member_with_edit_rights()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/whiteboards", new { name = "Team board", isPrivate = false });
        var wb = (await create.Content.ReadFromJsonAsync<WhiteboardResp>())!;

        var ownerCheck = await owner.GetFromJsonAsync<WhiteboardCollabAccessResp>($"/api/v1/internal/whiteboards/{wb.Id}/can-collaborate");
        ownerCheck!.Allowed.ShouldBeTrue();
        ownerCheck.CanEdit.ShouldBeTrue();

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "wbcc2");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);
        var memberCheck = await member.GetFromJsonAsync<WhiteboardCollabAccessResp>($"/api/v1/internal/whiteboards/{wb.Id}/can-collaborate");
        memberCheck!.Allowed.ShouldBeTrue();
        memberCheck.CanEdit.ShouldBeTrue();
    }

    [Fact]
    public async Task Can_collaborate_denies_a_caller_from_a_completely_different_workspace()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/whiteboards", new { name = "Board A", isPrivate = false });
        var wb = (await create.Content.ReadFromJsonAsync<WhiteboardResp>())!;

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var response = await otherClient.GetAsync(new Uri($"/api/v1/internal/whiteboards/{wb.Id}/can-collaborate", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var access = (await response.Content.ReadFromJsonAsync<WhiteboardCollabAccessResp>())!;
        access.Allowed.ShouldBeFalse();
    }

    // ---- SECURITY: a Whiteboard linked to a private Task must be exactly as hidden as that Task ----

    [Fact]
    public async Task Whiteboard_linked_to_a_private_task_is_inaccessible_to_an_ungranted_member_and_accessible_once_granted()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "wbtask");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "CONFIDENTIAL-SPRINT-TASK");

        (await owner.PatchAsJsonAsync($"/api/v1/resources/task/{task.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var createLinked = await owner.PostAsJsonAsync("/api/v1/whiteboards", new { name = "Task plan", isPrivate = false, linkedResourceType = "task", linkedResourceId = task.Id });
        createLinked.StatusCode.ShouldBe(HttpStatusCode.Created);
        var wb = (await createLinked.Content.ReadFromJsonAsync<WhiteboardResp>())!;

        // The owner (who can see the private task) can still see the linked whiteboard.
        (await owner.GetAsync(new Uri($"/api/v1/whiteboards/{wb.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The ungranted member cannot see the task, so the linked whiteboard must be equally hidden — both
        // via direct GET (403/404, never leaking existence details beyond NotFound) and the workspace list.
        var memberGet = await member.GetAsync(new Uri($"/api/v1/whiteboards/{wb.Id}", UriKind.Relative));
        memberGet.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var memberList = await member.GetFromJsonAsync<List<WhiteboardResp>>("/api/v1/whiteboards");
        memberList!.ShouldNotContain(w => w.Id == wb.Id);

        // Once granted View access to the underlying task, the member can see the linked whiteboard too —
        // proving the gate really does track the linked resource's live ACL, not a cached snapshot.
        var members = await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members");
        var memberUserId = members!.First(m => m.Role == "Member").UserId;
        (await owner.PostAsJsonAsync($"/api/v1/resources/task/{task.Id}/permissions",
                new { principalType = "user", principalId = memberUserId, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await member.GetAsync(new Uri($"/api/v1/whiteboards/{wb.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterGrantList = await member.GetFromJsonAsync<List<WhiteboardResp>>("/api/v1/whiteboards");
        afterGrantList!.ShouldContain(w => w.Id == wb.Id);
    }

    // ---- SECURITY: a Clip linked to a private Document must be exactly as hidden as that Document ----

    [Fact]
    public async Task Clip_linked_to_a_private_document_is_inaccessible_to_an_ungranted_member()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "clipdoc");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var docCreate = await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Runbook", content = "", isPrivate = true });
        var doc = (await docCreate.Content.ReadFromJsonAsync<DocResp>())!;

        var clip = await UploadClipAsync(owner, "Walkthrough", linkedResourceType: "document", linkedResourceId: doc.Id);

        // The owner (the document's private owner) can see the linked clip.
        (await owner.GetAsync(new Uri($"/api/v1/clips/{clip.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The ungranted member cannot view the private document, so the linked clip is equally hidden.
        var memberGet = await member.GetAsync(new Uri($"/api/v1/clips/{clip.Id}", UriKind.Relative));
        memberGet.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var memberList = await member.GetFromJsonAsync<List<ClipResp>>("/api/v1/clips");
        memberList!.ShouldNotContain(c => c.Id == clip.Id);

        // Comments and transcript requests are gated by the exact same rule.
        (await member.PostAsJsonAsync($"/api/v1/clips/{clip.Id}/comments", new { body = "nice!" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Private_clip_is_hidden_from_a_non_owner_workspace_member()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "clippriv");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var clip = await UploadClipAsync(owner, "My private clip", isPrivate: true);

        (await owner.GetAsync(new Uri($"/api/v1/clips/{clip.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.GetAsync(new Uri($"/api/v1/clips/{clip.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---- Upload/download/comments happy path ----

    [Fact]
    public async Task Clip_upload_download_and_comment_round_trip()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var clip = await UploadClipAsync(owner, "Standup recording", durationSeconds: 42.5);
        clip.SizeBytes.ShouldBeGreaterThan(0);
        clip.DurationSeconds.ShouldBe(42.5);
        clip.Status.ShouldBe("Ready");

        var download = await owner.GetAsync(new Uri($"/api/v1/clips/{clip.Id}/download", UriKind.Relative));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        // First 4 bytes are the EBML magic-byte prefix UploadClipAsync adds so the upload passes
        // FileContentValidator's webm content-type check — the round trip itself is
        // still the thing under test.
        Encoding.UTF8.GetString(bytes[4..]).ShouldBe("fake-clip-bytes");

        var comment = await owner.PostAsJsonAsync($"/api/v1/clips/{clip.Id}/comments", new { body = "Great session" });
        comment.StatusCode.ShouldBe(HttpStatusCode.Created);

        var comments = await owner.GetFromJsonAsync<List<Dictionary<string, object>>>($"/api/v1/clips/{clip.Id}/comments");
        comments!.Count.ShouldBe(1);
    }

    // ---- Transcription: no provider configured in tests -> honest "Unavailable", never a faked transcript ----

    [Fact]
    public async Task Transcription_request_returns_unavailable_when_no_ai_provider_is_configured()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var clip = await UploadClipAsync(owner, "Untranscribed clip");

        var response = await owner.PostAsync(new Uri($"/api/v1/clips/{clip.Id}/transcript", UriKind.Relative), null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ClipTranscriptResp>())!;
        body.Status.ShouldBe("Unavailable");
        body.Text.ShouldBeNull(); // never a faked transcript when no provider is configured

        var getResponse = await owner.GetFromJsonAsync<ClipTranscriptResp>($"/api/v1/clips/{clip.Id}/transcript");
        getResponse!.Status.ShouldBe("Unavailable");
    }

    // ---- SECURITY: a Clip transcript must be permission-filtered in cross-module search, identically ----

    [Fact]
    public async Task Clip_transcript_is_permission_filtered_in_search_results()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "clipsearch");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var privateClip = await UploadClipAsync(owner, "Leadership sync", isPrivate: true);
        await SeedReadyTranscriptAsync(privateClip.Id, workspaceId, "We discussed the ZEBRA-CODE-WORD acquisition plan.");

        var publicClip = await UploadClipAsync(owner, "All-hands recap");
        await SeedReadyTranscriptAsync(publicClip.Id, workspaceId, "General updates about the ZEBRA-CODE-WORD rollout timeline.");

        // The owner (who can see both) finds both via the transcript text.
        var ownerHits = (await owner.GetFromJsonAsync<List<SearchHitResp>>("/api/v1/search?q=ZEBRA-CODE-WORD"))!;
        ownerHits.Where(h => h.Type == "Clip").Select(h => h.Id).ShouldContain(privateClip.Id);
        ownerHits.Where(h => h.Type == "Clip").Select(h => h.Id).ShouldContain(publicClip.Id);

        // The ungranted member finds only the public clip's transcript — the private clip's transcript text
        // must never leak through search, exactly like Documents/Chat's search providers.
        var memberHits = await member.GetFromJsonAsync<List<SearchHitResp>>("/api/v1/search?q=ZEBRA-CODE-WORD");
        var memberClipHits = memberHits!.Where(h => h.Type == "Clip").Select(h => h.Id).ToList();
        memberClipHits.ShouldContain(publicClip.Id);
        memberClipHits.ShouldNotContain(privateClip.Id);

        var raw = await member.GetStringAsync("/api/v1/search?q=ZEBRA-CODE-WORD");
        raw.ShouldContain("General updates"); // the public clip's snippet is fine to see
    }

    private static async Task<ClipResp> UploadClipAsync(
        HttpClient client, string title, bool isPrivate = false, string? linkedResourceType = null, Guid? linkedResourceId = null, double? durationSeconds = null)
    {
        // Simple-type parameters bind from the query string, the file from the multipart body — same
        // convention ImportEndpoints' upload already established (see ImportersFlowTests.cs), not form
        // fields (minimal-API form binding needs an explicit [FromForm] for non-file parameters).
        var query = $"title={Uri.EscapeDataString(title)}&isPrivate={isPrivate}";
        if (linkedResourceType is not null)
        {
            query += $"&linkedResourceType={Uri.EscapeDataString(linkedResourceType)}&linkedResourceId={linkedResourceId}";
        }

        if (durationSeconds is not null)
        {
            query += $"&durationSeconds={durationSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        // EBML magic bytes (0x1A45DFA3) prefixed so this passes FileContentValidator's webm content-type
        // check — the rest of the "file" is still arbitrary filler, the API only sniffs
        // a short prefix.
        var clipBytes = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }.Concat(Encoding.UTF8.GetBytes("fake-clip-bytes")).ToArray();
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(clipBytes), "file", "clip.webm" },
        };

        var response = await client.PostAsync(new Uri($"/api/v1/clips?{query}", UriKind.Relative), form);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode} uploading clip: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<ClipResp>())!;
    }

    private async Task SeedReadyTranscriptAsync(Guid clipId, Guid workspaceId, string text)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO clips.clip_transcripts (id, workspace_id, clip_id, status, text, segments_json, created_at_utc, updated_at_utc)
            VALUES (@id, @workspaceId, @clipId, 'Ready', @text, NULL, now(), now())
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("workspaceId", workspaceId);
        command.Parameters.AddWithValue("clipId", clipId);
        command.Parameters.AddWithValue("text", text);
        await command.ExecuteNonQueryAsync();
    }
}
