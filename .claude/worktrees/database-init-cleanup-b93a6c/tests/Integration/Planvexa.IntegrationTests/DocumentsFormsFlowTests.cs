namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

// Response shapes for the documents + forms endpoints.
internal sealed record DocumentResp(Guid Id, string Title, string Content, bool IsPrivate, Guid OwnerUserId, Guid? SpaceId, Guid? ListId, Guid? TaskId, DateTimeOffset UpdatedAtUtc);
internal sealed record DocumentSummaryResp(Guid Id, string Title, bool IsPrivate, Guid OwnerUserId, Guid? SpaceId, Guid? ListId, Guid? TaskId, DateTimeOffset UpdatedAtUtc);
internal sealed record DocumentVersionResp(Guid Id, Guid AuthorUserId, DateTimeOffset CreatedAtUtc, string ContentPreview);
internal sealed record FormFieldResp(Guid Id, string Label, string Type, bool Required, List<string> Options, int Position);
internal sealed record FormResp(Guid Id, Guid ListId, string Title, string? Description, bool IsActive, string PublicToken, List<FormFieldResp> Fields);
internal sealed record PublicFormResp(string Title, string? Description, List<FormFieldResp> Fields);
internal sealed record SubmitResultResp(Guid SubmissionId, Guid? CreatedTaskId);
internal sealed record FormSubmissionResp(Guid Id, Guid? CreatedTaskId, DateTimeOffset SubmittedAtUtc, Dictionary<string, string> Values);

[Collection("api")]
public sealed class DocumentsFormsFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Document_crud_captures_versions_and_reverts()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var create = await client.PostAsJsonAsync("/api/v1/documents", new { title = "Spec", content = "v1", isPrivate = false });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var doc = (await create.Content.ReadFromJsonAsync<DocumentResp>())!;

        // Edit content twice → two more versions.
        await client.PatchAsJsonAsync($"/api/v1/documents/{doc.Id}", new { content = "v2" });
        await client.PatchAsJsonAsync($"/api/v1/documents/{doc.Id}", new { content = "v3" });

        var versions = await client.GetFromJsonAsync<List<DocumentVersionResp>>($"/api/v1/documents/{doc.Id}/versions");
        versions!.Count.ShouldBe(3); // initial + v2 + v3

        // Revert to the initial version (oldest).
        var initial = versions.OrderBy(v => v.CreatedAtUtc).First();
        var reverted = await client.PostAsync(new Uri($"/api/v1/documents/{doc.Id}/revert/{initial.Id}", UriKind.Relative), null);
        reverted.EnsureSuccessStatusCode();
        var afterRevert = await client.GetFromJsonAsync<DocumentResp>($"/api/v1/documents/{doc.Id}");
        afterRevert!.Content.ShouldBe("v1");
    }

    [Fact]
    public async Task Private_document_is_invisible_to_a_non_owner_member()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var create = await owner.PostAsJsonAsync("/api/v1/documents", new { title = "Private", content = "secret", isPrivate = true });
        var doc = (await create.Content.ReadFromJsonAsync<DocumentResp>())!;

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "dm");
        var member = fixture.WorkClient(memberSubject, slug, workspaceId);

        var list = await member.GetFromJsonAsync<List<DocumentSummaryResp>>("/api/v1/documents");
        list!.ShouldNotContain(d => d.Id == doc.Id);

        var direct = await member.GetAsync(new Uri($"/api/v1/documents/{doc.Id}", UriKind.Relative));
        direct.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Documents_are_isolated_between_tenants()
    {
        var (clientA, _, _, _) = await fixture.NewWorkspaceClientAsync();
        await clientA.PostAsJsonAsync("/api/v1/documents", new { title = "A-doc", content = "x", isPrivate = false });

        var (clientB, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var list = await clientB.GetFromJsonAsync<List<DocumentSummaryResp>>("/api/v1/documents");
        list!.ShouldNotContain(d => d.Title == "A-doc");
    }

    [Fact]
    public async Task Public_form_submission_creates_a_task_and_is_idempotent()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var create = await client.PostAsJsonAsync("/api/v1/forms", new
        {
            listId = list.Id,
            title = "Bug intake",
            description = "Report a bug",
            fields = new[]
            {
                new { label = "Summary", type = "Text", required = true, options = Array.Empty<string>(), position = 0 },
                new { label = "Details", type = "LongText", required = false, options = Array.Empty<string>(), position = 1 },
            },
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var form = (await create.Content.ReadFromJsonAsync<FormResp>())!;
        var summaryFieldId = form.Fields.Single(f => f.Label == "Summary").Id.ToString();

        // Anonymous fetch of the public form (no auth headers).
        var anon = fixture.Factory.CreateClient();
        var publicForm = await anon.GetFromJsonAsync<PublicFormResp>($"/api/v1/public/forms/{form.PublicToken}");
        publicForm!.Title.ShouldBe("Bug intake");

        // Submit with an idempotency key.
        var payload = new { values = new Dictionary<string, string> { [summaryFieldId] = "Login broken" } };
        anon.DefaultRequestHeaders.Add("Idempotency-Key", "submit-1");
        var submit1 = await anon.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}/submissions", payload);
        submit1.EnsureSuccessStatusCode();
        var result1 = (await submit1.Content.ReadFromJsonAsync<SubmitResultResp>())!;
        result1.CreatedTaskId.ShouldNotBeNull();

        // Repeat with the same key → same submission, no duplicate task.
        var submit2 = await anon.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}/submissions", payload);
        submit2.EnsureSuccessStatusCode();
        var result2 = (await submit2.Content.ReadFromJsonAsync<SubmitResultResp>())!;
        result2.SubmissionId.ShouldBe(result1.SubmissionId);
        result2.CreatedTaskId.ShouldBe(result1.CreatedTaskId);

        // The owner sees exactly one submission linked to the created task.
        var submissions = await client.GetFromJsonAsync<List<FormSubmissionResp>>($"/api/v1/forms/{form.Id}/submissions");
        submissions!.Count(s => s.CreatedTaskId == result1.CreatedTaskId).ShouldBe(1);

        // And the task exists in the list.
        var tasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks");
        tasks!.ShouldContain(t => t.Id == result1.CreatedTaskId!.Value);
    }

    [Fact]
    public async Task Inactive_form_rejects_public_submission()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var create = await client.PostAsJsonAsync("/api/v1/forms", new { listId = list.Id, title = "Closed", fields = Array.Empty<object>() });
        var form = (await create.Content.ReadFromJsonAsync<FormResp>())!;

        await client.PatchAsJsonAsync($"/api/v1/forms/{form.Id}", new { isActive = false });

        var anon = fixture.Factory.CreateClient();
        var get = await anon.GetAsync(new Uri($"/api/v1/public/forms/{form.PublicToken}", UriKind.Relative));
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Guest_cannot_author_forms_or_documents()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (guestSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "gf", role: "Guest");
        var guest = fixture.WorkClient(guestSubject, slug, workspaceId);

        var doc = await guest.PostAsJsonAsync("/api/v1/documents", new { title = "x", content = "y", isPrivate = false });
        doc.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var form = await guest.PostAsJsonAsync("/api/v1/forms", new { listId = Guid.NewGuid(), title = "x", fields = Array.Empty<object>() });
        form.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
