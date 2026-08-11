namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Response shapes for the documents/wikis endpoints.
internal sealed record DocResp(Guid Id, string Title, string Content, bool IsPrivate, Guid OwnerUserId, Guid? SpaceId, Guid? ListId, Guid? TaskId, Guid? ParentDocumentId, DateTimeOffset UpdatedAtUtc);
internal sealed record CollabAccessResp(bool Allowed, bool CanEdit, Guid? UserId);
internal sealed record DocTemplateResp(Guid Id, string Name, DateTimeOffset CreatedAtUtc);

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
}
