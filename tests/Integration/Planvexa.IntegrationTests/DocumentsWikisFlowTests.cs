namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Response shapes for the documents/wikis endpoints.
internal sealed record DocResp(Guid Id, string Title, string Content, bool IsPrivate, Guid OwnerUserId, Guid? SpaceId, Guid? ListId, Guid? TaskId, Guid? ParentDocumentId, DateTimeOffset UpdatedAtUtc);
internal sealed record DocPermissionResp(Guid Id, string ResourceType, Guid ResourceId, string PrincipalType, Guid PrincipalId, string Level, Guid GrantedByUserId, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc);
internal sealed record CollabAccessResp(bool Allowed, bool CanEdit, Guid? UserId);
internal sealed record DocTemplateResp(Guid Id, string Name, DateTimeOffset CreatedAtUtc);
internal sealed record DocImageUploadResp(Guid ImageId, string ContentType);
internal sealed record DocAttachmentUploadResp(Guid AttachmentId, string FileName, string ContentType, long SizeBytes);
internal sealed record DocCommentResp(Guid Id, Guid AuthorUserId, string Body, DateTimeOffset CreatedAtUtc);
internal sealed record DocShareResp(Guid Id, Guid DocumentId, string Token, string Url, DateTimeOffset? ExpiresAtUtc, bool RequiresPassword);
internal sealed record SharedDocResp(Guid DocumentId, string Title, string ContentMarkdown, DateTimeOffset UpdatedAtUtc);

[Collection("api")]
public sealed class DocumentsWikisFlowTests(PlanvexaFixture fixture)
{
    // ---- (CRITICAL): collaboration-room authorization ----
    // The Hocuspocus server's onAuthenticate hook calls GET /api/v1/internal/documents/{id}/can-collaborate
    // with the connecting user's own bearer token before admitting them to a document's WebSocket room.
    // These two tests are the load-bearing proof that endpoint actually enforces per-document access
    // rather than merely "is this a valid user" — matching the five prior confidentiality bugs this
    // roadmap has already found in listing/broadcast paths that skipped per-resource permission filtering.

    [Fact]
    public async Task Can_collaborate_denies_a_non_owner_member_on_a_private_document()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Secret plans", content = "", isPrivate = true });
        var doc = (await create.Content.ReadFromJsonAsync<DocResp>())!;

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "cc1");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var response = await member.GetAsync(new Uri($"/api/v1/internal/documents/{doc.Id}/can-collaborate", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK); // the endpoint itself succeeds; access is signalled in the payload
        var access = (await response.Content.ReadFromJsonAsync<CollabAccessResp>())!;
        access.Allowed.ShouldBeFalse();
        access.CanEdit.ShouldBeFalse();
    }

    [Fact]
    public async Task Can_collaborate_allows_the_owner_and_a_shared_document_member_with_edit_rights()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Runbook", content = "", isPrivate = false });
        var doc = (await create.Content.ReadFromJsonAsync<DocResp>())!;

        // Owner: allowed and editable.
        var ownerCheck = await owner.GetFromJsonAsync<CollabAccessResp>($"/api/v1/internal/documents/{doc.Id}/can-collaborate");
        ownerCheck!.Allowed.ShouldBeTrue();
        ownerCheck.CanEdit.ShouldBeTrue();

        // A regular member of the (non-private) document's workspace: also allowed/editable — this is a
        // shared document, so workspace membership plus DocumentsAuthorizer.CanEdit is sufficient, matching
        // DocumentService.UpdateAsync's own rule.
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "cc2");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);
        var memberCheck = await member.GetFromJsonAsync<CollabAccessResp>($"/api/v1/internal/documents/{doc.Id}/can-collaborate");
        memberCheck!.Allowed.ShouldBeTrue();
        memberCheck.CanEdit.ShouldBeTrue();
    }

    [Fact]
    public async Task Can_collaborate_denies_a_guest_of_a_completely_different_workspace()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Doc A", content = "", isPrivate = false });
        var doc = (await create.Content.ReadFromJsonAsync<DocResp>())!;

        // A user who creates their own, unrelated workspace and tries to hit the endpoint scoped to their
        // OWN workspace header must be denied — the check must key off the document's actual owning
        // workspace, not merely "caller has some workspace".
        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var response = await otherClient.GetAsync(new Uri($"/api/v1/internal/documents/{doc.Id}/can-collaborate", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var access = (await response.Content.ReadFromJsonAsync<CollabAccessResp>())!;
        access.Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Can_collaborate_denies_a_guest_edit_but_not_necessarily_read()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Shared doc", content = "", isPrivate = false });
        var doc = (await create.Content.ReadFromJsonAsync<DocResp>())!;

        var (guestSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "cc3", role: "Guest");
        var guest = fixture.WorkClient(guestSubject, slug, workspaceId);

        var access = await guest.GetFromJsonAsync<CollabAccessResp>($"/api/v1/internal/documents/{doc.Id}/can-collaborate");
        access!.Allowed.ShouldBeTrue(); // guests are read-only members, still allowed into the room
        access.CanEdit.ShouldBeFalse(); // but never editable
    }

    // ----: document hierarchy cycle prevention via the real API ----

    [Fact]
    public async Task Reparenting_a_document_under_its_own_descendant_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var root = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Root", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;
        var child = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Child", content = "", parentDocumentId = root.Id })).Content.ReadFromJsonAsync<DocResp>())!;

        var attempt = await client.PostAsJsonAsync($"/api/v1/documents/{root.Id}/parent", new { parentDocumentId = child.Id });
        attempt.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reparenting_a_document_under_itself_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Solo", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var attempt = await client.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/parent", new { parentDocumentId = doc.Id });
        attempt.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Valid_reparenting_moves_the_document_and_is_reflected_on_read()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var folderDoc = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Wiki root", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;
        var page = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Page", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var moved = await client.PostAsJsonAsync($"/api/v1/documents/{page.Id}/parent", new { parentDocumentId = folderDoc.Id });
        moved.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<DocResp>($"/api/v1/documents/{page.Id}");
        after!.ParentDocumentId.ShouldBe(folderDoc.Id);
    }

    // ----: search respects document privacy with the new Lexical content shape ----

    [Fact]
    public async Task Search_matches_document_content_but_still_hides_a_private_document_from_a_non_owner()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var marker = $"zeta{Guid.NewGuid():N}"[..12];

        var lexicalContent = "{\"root\":{\"children\":[{\"type\":\"paragraph\",\"children\":[{\"type\":\"text\",\"text\":\"" + marker + " secret payload\"}]}],\"type\":\"root\"}}";
        var priv = await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Private note", content = lexicalContent, isPrivate = true });
        priv.EnsureSuccessStatusCode();

        var sharedContent = "{\"root\":{\"children\":[{\"type\":\"paragraph\",\"children\":[{\"type\":\"text\",\"text\":\"" + marker + " shared payload\"}]}],\"type\":\"root\"}}";
        var shared = await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Shared note", content = sharedContent, isPrivate = false });
        shared.EnsureSuccessStatusCode();

        // Owner sees both via search-over-content.
        var ownerResults = await owner.GetFromJsonAsync<List<SearchResp>>($"/api/v1/search?q={marker}");
        ownerResults!.Count(r => r.Type == "Document").ShouldBe(2);

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "sr1");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);
        var memberResults = await member.GetFromJsonAsync<List<SearchResp>>($"/api/v1/search?q={marker}");
        var memberDocHits = memberResults!.Where(r => r.Type == "Document").ToList();
        memberDocHits.Count.ShouldBe(1); // only the shared one
        memberDocHits.Single().Title.ShouldBe("Shared note");
    }

    // ----: Markdown export ----

    [Fact]
    public async Task Export_renders_the_lexical_content_as_markdown()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        const string content = "{\"root\":{\"children\":[{\"type\":\"heading\",\"tag\":\"h1\",\"children\":[{\"type\":\"text\",\"text\":\"Hello\"}]},{\"type\":\"paragraph\",\"children\":[{\"type\":\"text\",\"text\":\"World\",\"format\":1}]}],\"type\":\"root\"}}";
        var create = await client.PostAsJsonAsync("/api/v1/documents", new { title = "Export me", content });
        var doc = (await create.Content.ReadFromJsonAsync<DocResp>())!;

        var response = await client.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/export", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        var markdown = await response.Content.ReadAsStringAsync();
        markdown.ShouldContain("# Export me"); // title heading
        markdown.ShouldContain("# Hello");
        markdown.ShouldContain("**World**");
    }

    // ----: templates ----

    [Fact]
    public async Task Creating_a_document_from_a_template_seeds_its_content()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        const string content = "{\"root\":{\"children\":[{\"type\":\"paragraph\",\"children\":[{\"type\":\"text\",\"text\":\"Template body\"}]}],\"type\":\"root\"}}";
        var source = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Source", content })).Content.ReadFromJsonAsync<DocResp>())!;

        var templateResponse = await client.PostAsJsonAsync($"/api/v1/document-templates/from-document/{source.Id}", new { name = "Meeting notes" });
        templateResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var template = (await templateResponse.Content.ReadFromJsonAsync<DocTemplateResp>())!;

        var list = await client.GetFromJsonAsync<List<DocTemplateResp>>("/api/v1/document-templates");
        list!.ShouldContain(t => t.Id == template.Id);

        var fromTemplate = await client.PostAsJsonAsync("/api/v1/documents", new { title = "New meeting", content = "", templateId = template.Id });
        fromTemplate.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await fromTemplate.Content.ReadFromJsonAsync<DocResp>())!;
        created.Content.ShouldBe(content);
    }

    // ---- Images embedded in document content ----

    [Fact]
    public async Task Image_upload_and_download_round_trip()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "With image", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var uploaded = await UploadImageAsync(client, doc.Id);
        uploaded.ContentType.ShouldBe("image/png");

        var download = await client.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/images/{uploaded.ImageId}", UriKind.Relative));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        bytes.ShouldBe(PngBytes());
    }

    [Fact]
    public async Task Image_upload_is_rejected_for_a_document_in_a_different_workspace()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Workspace A doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var response = await UploadImageResponseAsync(otherClient, doc.Id);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound); // RLS + EnsureInWorkspace hide the document entirely
    }

    [Fact]
    public async Task Image_download_is_rejected_for_a_document_in_a_different_workspace()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Workspace A doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;
        var uploaded = await UploadImageAsync(owner, doc.Id);

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var response = await otherClient.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/images/{uploaded.ImageId}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Image_upload_and_download_are_rejected_for_a_non_owner_member_on_a_private_document()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Secret doc", content = "", isPrivate = true })).Content.ReadFromJsonAsync<DocResp>())!;
        var uploaded = await UploadImageAsync(owner, doc.Id);

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "docimg1");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var uploadResponse = await UploadImageResponseAsync(member, doc.Id);
        uploadResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var downloadResponse = await member.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/images/{uploaded.ImageId}", UriKind.Relative));
        downloadResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static byte[] PngBytes()
        // PNG magic-byte signature so this passes FileContentValidator's image/png content-type check.
        => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static async Task<DocImageUploadResp> UploadImageAsync(HttpClient client, Guid documentId)
    {
        var response = await UploadImageResponseAsync(client, documentId);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode} uploading document image: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<DocImageUploadResp>())!;
    }

    private static async Task<HttpResponseMessage> UploadImageResponseAsync(HttpClient client, Guid documentId)
    {
        var part = new ByteArrayContent(PngBytes());
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        using var form = new MultipartFormDataContent { { part, "file", "diagram.png" } };

        return await client.PostAsync(new Uri($"/api/v1/documents/{documentId}/images", UriKind.Relative), form);
    }

    // ---- File attachments embedded in document content ----

    [Fact]
    public async Task Attachment_upload_and_download_round_trip()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "With attachment", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var uploaded = await UploadAttachmentAsync(client, doc.Id, "report.pdf", "application/pdf", PdfBytes());
        uploaded.FileName.ShouldBe("report.pdf");
        uploaded.ContentType.ShouldBe("application/pdf");
        uploaded.SizeBytes.ShouldBe(PdfBytes().Length);

        var download = await client.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/attachments/{uploaded.AttachmentId}/{uploaded.FileName}", UriKind.Relative));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        var disposition = download.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        disposition!.DispositionType.ShouldBe("attachment"); // forces a download rather than inline rendering
        (disposition.FileNameStar ?? disposition.FileName)!.ShouldContain("report.pdf");
        var bytes = await download.Content.ReadAsByteArrayAsync();
        bytes.ShouldBe(PdfBytes());
    }

    [Fact]
    public async Task Attachment_upload_sanitizes_a_path_traversal_file_name()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "With attachment", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var uploaded = await UploadAttachmentAsync(client, doc.Id, "../../etc/passwd.pdf", "application/pdf", PdfBytes());
        uploaded.FileName.ShouldBe("passwd.pdf"); // directory components stripped, no path traversal survives
    }

    [Fact]
    public async Task Attachment_upload_is_rejected_for_a_document_in_a_different_workspace()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Workspace A doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var response = await UploadAttachmentResponseAsync(otherClient, doc.Id, "report.pdf", "application/pdf", PdfBytes());
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound); // RLS + EnsureInWorkspace hide the document entirely
    }

    [Fact]
    public async Task Attachment_download_is_rejected_for_a_document_in_a_different_workspace()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Workspace A doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;
        var uploaded = await UploadAttachmentAsync(owner, doc.Id, "report.pdf", "application/pdf", PdfBytes());

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var response = await otherClient.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/attachments/{uploaded.AttachmentId}/{uploaded.FileName}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Attachment_upload_and_download_are_rejected_for_a_non_owner_member_on_a_private_document()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Secret doc", content = "", isPrivate = true })).Content.ReadFromJsonAsync<DocResp>())!;
        var uploaded = await UploadAttachmentAsync(owner, doc.Id, "report.pdf", "application/pdf", PdfBytes());

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "docatt1");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var uploadResponse = await UploadAttachmentResponseAsync(member, doc.Id, "other.pdf", "application/pdf", PdfBytes());
        uploadResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var downloadResponse = await member.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/attachments/{uploaded.AttachmentId}/{uploaded.FileName}", UriKind.Relative));
        downloadResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---- Comments on documents ----

    [Fact]
    public async Task Comment_add_and_list_round_trip_on_a_shared_document()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Discuss me", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var posted = await client.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/comments", new { body = "First thoughts" });
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var comment = (await posted.Content.ReadFromJsonAsync<DocCommentResp>())!;
        comment.Body.ShouldBe("First thoughts");

        var list = await client.GetFromJsonAsync<List<DocCommentResp>>($"/api/v1/documents/{doc.Id}/comments");
        list!.ShouldContain(c => c.Id == comment.Id && c.Body == "First thoughts");
    }

    [Fact]
    public async Task A_workspace_member_can_read_and_add_comments_on_another_members_shared_document()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Shared doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "doccm1");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var posted = await member.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/comments", new { body = "Looks good" });
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);

        var ownerList = await owner.GetFromJsonAsync<List<DocCommentResp>>($"/api/v1/documents/{doc.Id}/comments");
        ownerList!.ShouldContain(c => c.Body == "Looks good");
    }

    [Fact]
    public async Task A_guest_can_read_comments_but_cannot_post_one()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Guest-visible doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;
        await owner.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/comments", new { body = "Owner note" });

        var (guestSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "doccm2", role: "Guest");
        var guest = fixture.WorkClient(guestSubject, slug, workspaceId);

        var list = await guest.GetFromJsonAsync<List<DocCommentResp>>($"/api/v1/documents/{doc.Id}/comments");
        list!.ShouldContain(c => c.Body == "Owner note");

        var attempt = await guest.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/comments", new { body = "Guest reply" });
        attempt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Comment_read_and_add_are_forbidden_for_a_non_owner_member_on_a_private_document()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Secret doc", content = "", isPrivate = true })).Content.ReadFromJsonAsync<DocResp>())!;
        await owner.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/comments", new { body = "Owner-only note" });

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "doccm3");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var listAttempt = await member.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/comments", UriKind.Relative));
        listAttempt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var postAttempt = await member.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/comments", new { body = "Should not land" });
        postAttempt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Comment_read_and_add_are_rejected_for_a_document_in_a_different_workspace()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Workspace A doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var listAttempt = await otherClient.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/comments", UriKind.Relative));
        listAttempt.StatusCode.ShouldBe(HttpStatusCode.NotFound); // RLS + workspace check hide the document entirely

        var postAttempt = await otherClient.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/comments", new { body = "Should not land" });
        postAttempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- Public share links ----

    [Fact]
    public async Task Public_link_returns_only_the_shared_documents_markdown_and_404_after_revoke()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        const string content = "{\"root\":{\"children\":[{\"type\":\"heading\",\"tag\":\"h1\",\"children\":[{\"type\":\"text\",\"text\":\"Hello\"}]}],\"type\":\"root\"}}";
        var doc = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Shared doc", content })).Content.ReadFromJsonAsync<DocResp>())!;
        var otherDoc = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Other doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var shareResponse = await client.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/share", new { expiresInDays = 7 });
        shareResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var share = (await shareResponse.Content.ReadFromJsonAsync<DocShareResp>())!;
        share.RequiresPassword.ShouldBeFalse();

        // Anonymous client (no auth headers) can read ONLY the shared document, rendered as Markdown.
        var anon = fixture.Factory.CreateClient();
        var publicResponse = await anon.GetAsync(new Uri($"/api/v1/public/documents/{share.Token}", UriKind.Relative));
        publicResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var shared = await publicResponse.Content.ReadFromJsonAsync<SharedDocResp>();
        shared!.DocumentId.ShouldBe(doc.Id);
        shared.Title.ShouldBe("Shared doc");
        shared.ContentMarkdown.ShouldContain("# Hello");

        // A made-up token for the other document is not accessible.
        (await anon.GetAsync(new Uri($"/api/v1/public/documents/{Guid.NewGuid():N}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _ = otherDoc;

        // Listing redacts the token.
        var list = await client.GetFromJsonAsync<List<DocShareResp>>($"/api/v1/documents/{doc.Id}/shares");
        list!.ShouldContain(s => s.Id == share.Id && s.Token == string.Empty);

        // Revoke → public read now 404s.
        (await client.DeleteAsync(new Uri($"/api/v1/document-shares/{share.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anon.GetAsync(new Uri($"/api/v1/public/documents/{share.Token}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Password_protected_document_link_requires_the_correct_password()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await client.PostAsJsonAsync("/api/v1/documents", new { title = "Guarded doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;

        var shareResponse = await client.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/share", new { password = "hunter2" });
        var share = (await shareResponse.Content.ReadFromJsonAsync<DocShareResp>())!;
        share.RequiresPassword.ShouldBeTrue();

        var anon = fixture.Factory.CreateClient();

        var noPassword = await anon.GetAsync(new Uri($"/api/v1/public/documents/{share.Token}", UriKind.Relative));
        noPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var wrongPassword = await anon.GetAsync(new Uri($"/api/v1/public/documents/{share.Token}?password=nope", UriKind.Relative));
        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var rightPassword = await anon.GetAsync(new Uri($"/api/v1/public/documents/{share.Token}?password=hunter2", UriKind.Relative));
        rightPassword.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sharing_a_private_document_is_forbidden_for_a_non_owner_member()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Secret doc", content = "", isPrivate = true })).Content.ReadFromJsonAsync<DocResp>())!;

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "docshare1");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var attempt = await member.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/share", new { });
        attempt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Creating_and_revoking_a_share_link_for_a_document_in_a_different_workspace_is_rejected()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Workspace A doc", content = "" })).Content.ReadFromJsonAsync<DocResp>())!;
        var share = (await (await owner.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/share", new { })).Content.ReadFromJsonAsync<DocShareResp>())!;

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var shareAttempt = await otherClient.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/share", new { });
        shareAttempt.StatusCode.ShouldBe(HttpStatusCode.NotFound); // RLS + EnsureInWorkspace hide the document entirely

        var revokeAttempt = await otherClient.DeleteAsync(new Uri($"/api/v1/document-shares/{share.Id}", UriKind.Relative));
        revokeAttempt.StatusCode.ShouldBe(HttpStatusCode.NotFound); // RLS hides the other workspace's share link entirely
    }

    // ---- Private sharing with specific Users/Teams (ADR-0003) ----

    [Fact]
    public async Task A_private_document_is_invisible_to_a_member_until_granted_view_access()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Confidential", content = "", isPrivate = true }))
            .Content.ReadFromJsonAsync<DocResp>())!;

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "docshareA");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        // Baseline: hidden from the list and from a direct GET.
        (await member.GetFromJsonAsync<List<DocResp>>("/api/v1/documents"))!.ShouldNotContain(d => d.Id == doc.Id);
        (await member.GetAsync(new Uri($"/api/v1/documents/{doc.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var grant = await owner.PostAsJsonAsync(
            $"/api/v1/documents/{doc.Id}/permissions", new { principalType = "user", principalId = memberUserId, level = "view" });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Now visible in the list and via direct GET.
        (await member.GetFromJsonAsync<List<DocResp>>("/api/v1/documents"))!.ShouldContain(d => d.Id == doc.Id);
        (await member.GetAsync(new Uri($"/api/v1/documents/{doc.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Revoking the grant hides it again.
        var revoke = await owner.DeleteAsync(new Uri($"/api/v1/documents/{doc.Id}/permissions/user/{memberUserId}", UriKind.Relative));
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.GetAsync(new Uri($"/api/v1/documents/{doc.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_view_grant_does_not_allow_editing_but_an_edit_grant_does()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Confidential", content = "", isPrivate = true }))
            .Content.ReadFromJsonAsync<DocResp>())!;

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "docshareB");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        (await owner.PostAsJsonAsync(
                $"/api/v1/documents/{doc.Id}/permissions", new { principalType = "user", principalId = memberUserId, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // View-only: can read, cannot update.
        (await member.GetAsync(new Uri($"/api/v1/documents/{doc.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await member.PatchAsJsonAsync($"/api/v1/documents/{doc.Id}", new { title = "Hacked" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Upgrading to an edit grant allows the update.
        (await owner.PostAsJsonAsync(
                $"/api/v1/documents/{doc.Id}/permissions", new { principalType = "user", principalId = memberUserId, level = "edit" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var updated = await member.PatchAsJsonAsync($"/api/v1/documents/{doc.Id}", new { title = "Updated by grantee" });
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<DocResp>())!.Title.ShouldBe("Updated by grantee");
    }

    [Fact]
    public async Task A_non_owner_member_cannot_grant_list_or_revoke_sharing_on_another_members_document()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Confidential", content = "", isPrivate = true }))
            .Content.ReadFromJsonAsync<DocResp>())!;

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "docshareC");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);
        var (otherSubject, otherUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "docshareD");

        (await member.PostAsJsonAsync(
                $"/api/v1/documents/{doc.Id}/permissions", new { principalType = "user", principalId = otherUserId, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/permissions", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.DeleteAsync(new Uri($"/api/v1/documents/{doc.Id}/permissions/user/{memberUserId}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _ = otherSubject;
    }

    [Fact]
    public async Task Granting_or_listing_sharing_for_a_document_in_a_different_workspace_is_rejected()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Workspace A doc", content = "" }))
            .Content.ReadFromJsonAsync<DocResp>())!;

        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        (await otherClient.PostAsJsonAsync(
                $"/api/v1/documents/{doc.Id}/permissions", new { principalType = "user", principalId = Guid.NewGuid(), level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound); // RLS + workspace check hide the document entirely
        (await otherClient.GetAsync(new Uri($"/api/v1/documents/{doc.Id}/permissions", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _ = workspaceId;
    }

    [Fact]
    public async Task An_admin_can_manage_sharing_on_another_members_private_document()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var doc = (await (await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Confidential", content = "", isPrivate = true }))
            .Content.ReadFromJsonAsync<DocResp>())!;

        var (adminSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "docshareE", role: "Admin");
        var admin = fixture.WorkClient(adminSubject, slug, workspaceId);
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "docshareF");

        var grant = await admin.PostAsJsonAsync(
            $"/api/v1/documents/{doc.Id}/permissions", new { principalType = "user", principalId = memberUserId, level = "view" });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        var listResponse = await admin.GetFromJsonAsync<List<DocPermissionResp>>($"/api/v1/documents/{doc.Id}/permissions");
        listResponse!.ShouldContain(g => g.PrincipalId == memberUserId && string.Equals(g.Level, "view", StringComparison.OrdinalIgnoreCase));
        _ = memberSubject;
    }

    private static byte[] PdfBytes()
        // PDF magic-byte signature so this passes FileContentValidator's application/pdf content-type check.
        => "%PDF-1.4 fake content"u8.ToArray();

    private static async Task<DocAttachmentUploadResp> UploadAttachmentAsync(HttpClient client, Guid documentId, string fileName, string contentType, byte[] bytes)
    {
        var response = await UploadAttachmentResponseAsync(client, documentId, fileName, contentType, bytes);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode} uploading document attachment: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<DocAttachmentUploadResp>())!;
    }

    private static async Task<HttpResponseMessage> UploadAttachmentResponseAsync(HttpClient client, Guid documentId, string fileName, string contentType, byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var form = new MultipartFormDataContent { { part, "file", fileName } };

        return await client.PostAsync(new Uri($"/api/v1/documents/{documentId}/attachments", UriKind.Relative), form);
    }
}
