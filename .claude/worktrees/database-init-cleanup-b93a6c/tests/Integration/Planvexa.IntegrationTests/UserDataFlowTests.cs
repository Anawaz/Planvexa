namespace Planvexa.IntegrationTests;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Modules.Collaboration.Domain;
using Planvexa.Modules.Identity.Domain;
using Planvexa.Modules.Integrations.Domain;
using Planvexa.Modules.WorkManagement.Domain;
using Shouldly;
using Xunit;

internal sealed record UserDeletionSummaryResp(Guid UserId, int PersonalAccessTokensDeleted, DateTimeOffset AnonymizedAtUtc);

/// <summary>
/// The GDPR-style, self-service "export my data" / "delete my account" flow
/// (GET/DELETE /api/v1/users/me[/export]). See UserDataService's doc comment for why this is
/// self-service only (no Workspace-Owner-on-behalf-of-a-member model).
/// </summary>
[Collection("api")]
public sealed class UserDataFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Export_contains_the_callers_own_tasks_comments_time_entries_and_memberships()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "My own task");

        var commentResponse = await client.PostAsJsonAsync(
            $"/api/v1/tasks/{task.Id}/comments", new { body = "My own comment", parentId = (Guid?)null, mentionUserIds = (List<Guid>?)null });
        commentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var startResponse = await client.PostAsJsonAsync("/api/v1/timers/start", new { taskId = task.Id, description = "working" });
        startResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/v1/timers/stop", new { description = "done" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await client.GetAsync(new Uri("/api/v1/users/me/export", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.Name).ToHashSet();
        names.ShouldBe(["profile.json", "workspace-memberships.json", "tasks.json", "comments.json", "time-entries.json"], ignoreOrder: true);

        var memberships = await ReadJsonEntryAsync(zip, "workspace-memberships.json");
        memberships.EnumerateArray().Any(m => m.GetProperty("workspaceId").GetGuid() == workspaceId).ShouldBeTrue();

        var tasks = await ReadJsonEntryAsync(zip, "tasks.json");
        tasks.EnumerateArray().Any(t => t.GetProperty("taskId").GetGuid() == task.Id && t.GetProperty("relationship").GetString() == "Created")
            .ShouldBeTrue();

        var comments = await ReadJsonEntryAsync(zip, "comments.json");
        comments.EnumerateArray().Any(c => c.GetProperty("body").GetString() == "My own comment").ShouldBeTrue();

        var timeEntries = await ReadJsonEntryAsync(zip, "time-entries.json");
        timeEntries.EnumerateArray().Any(t => t.GetProperty("taskId").GetGuid() == task.Id).ShouldBeTrue();
    }

    /// <summary>
    /// Negative/isolation test (AGENTS.md rule 11): two members of the SAME Workspace each export their
    /// own data. Neither export may contain the other member's task/comment, even though both rows sit in
    /// the same Workspace the caller can otherwise see — the export is scoped by author identity, not by
    /// Workspace membership. There is no route parameter naming a target user (self-service only), so the
    /// only way to prove isolation is exactly this: content-level scoping.
    /// </summary>
    [Fact]
    public async Task Export_does_not_include_another_members_data()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, _) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        var space = await ownerClient.CreateSpaceAsync();
        var list = await ownerClient.CreateListAsync(space.Id);
        var memberTask = await memberClient.CreateTaskAsync(list.Id, "Member's task");
        (await memberClient.PostAsJsonAsync(
                $"/api/v1/tasks/{memberTask.Id}/comments", new { body = "Member's comment", parentId = (Guid?)null, mentionUserIds = (List<Guid>?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await ownerClient.GetAsync(new Uri("/api/v1/users/me/export", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);

        var tasks = await ReadJsonEntryAsync(zip, "tasks.json");
        tasks.EnumerateArray().Any(t => t.GetProperty("taskId").GetGuid() == memberTask.Id).ShouldBeFalse();

        var comments = await ReadJsonEntryAsync(zip, "comments.json");
        comments.EnumerateArray().Any(c => c.GetProperty("body").GetString() == "Member's comment").ShouldBeFalse();
    }

    [Fact]
    public async Task Sole_workspace_owner_cannot_delete_their_account()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var response = await client.DeleteAsync(new Uri("/api/v1/users/me", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// The full deletion flow, verified against actual DB state (not just the HTTP response): the PAT
    /// row is gone (hard delete), the User row is anonymized in place (PII scrubbed, same Id), and the
    /// task/comment rows the member authored are UNCHANGED — same row, same content, same
    /// author/creator UserId — proving anonymization does not touch other modules' tables (see
    /// User.IsAnonymized's doc comment for why that is the design).
    /// </summary>
    [Fact]
    public async Task Delete_hard_deletes_pats_and_anonymizes_the_profile_while_leaving_task_and_comment_rows_intact()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "deleteme");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        var space = await ownerClient.CreateSpaceAsync();
        var list = await ownerClient.CreateListAsync(space.Id);
        var task = await memberClient.CreateTaskAsync(list.Id, "Task before deletion");
        var commentResponse = await memberClient.PostAsJsonAsync(
            $"/api/v1/tasks/{task.Id}/comments", new { body = "Comment before deletion", parentId = (Guid?)null, mentionUserIds = (List<Guid>?)null });
        commentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var commentId = (await commentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var tokenResponse = await memberClient.PostAsJsonAsync("/api/v1/tokens", new { name = "ci-token", scopes = (List<string>?)null, expiresAtUtc = (DateTimeOffset?)null });
        tokenResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var deleteResponse = await memberClient.DeleteAsync(new Uri("/api/v1/users/me", UriKind.Relative));
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await deleteResponse.Content.ReadFromJsonAsync<UserDeletionSummaryResp>();
        summary!.PersonalAccessTokensDeleted.ShouldBe(1);

        // Workspace-owned tables need the ambient Workspace context the app connection's RLS requires
        // (see TenantIsolationDbTests' SetAmbient) — impersonate the (still-active) owner to read them.
        // Resolved via the authenticated API (not a raw DB query) because tenancy.workspace_members'
        // own bootstrap read policy requires an ambient app.current_user in the first place.
        var members = await ownerClient.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members");
        var ownerId = members!.Single(m => m.Role == "Owner").UserId;

        using var scope = fixture.Factory.Services.CreateScope();
        SetAmbient(scope, workspaceId, ownerId);
        var db = scope.ServiceProvider.GetRequiredService<PlanvexaDbContext>();

        // identity.users has no RLS (global table) — readable regardless of the ambient Workspace.
        var anonymized = await db.Set<User>().SingleAsync(u => u.Id == memberUserId);
        anonymized.IsAnonymized.ShouldBeTrue();
        anonymized.DisplayName.ShouldBe("Deleted User");
        anonymized.Email.ShouldStartWith("deleted-");
        anonymized.Subject.ShouldStartWith("deleted-");
        anonymized.IsActive.ShouldBeFalse();

        var patCount = await db.Set<PersonalAccessToken>().CountAsync(p => p.UserId == memberUserId);
        patCount.ShouldBe(0);

        var taskRow = await db.Set<WorkItem>().SingleAsync(t => t.Id == task.Id);
        taskRow.Title.ShouldBe("Task before deletion");
        taskRow.CreatedByUserId.ShouldBe(memberUserId);
        taskRow.IsDeleted.ShouldBeFalse();

        var commentRow = await db.Set<Comment>().SingleAsync(c => c.Id == commentId);
        commentRow.Body.ShouldBe("Comment before deletion");
        commentRow.AuthorUserId.ShouldBe(memberUserId);
        commentRow.IsDeleted.ShouldBeFalse();

        // The owner's own account is untouched by the member's deletion.
        var owner = await db.Set<User>().SingleAsync(u => u.Id == ownerId);
        owner.IsAnonymized.ShouldBeFalse();
    }

    private static async Task<JsonElement> ReadJsonEntryAsync(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName) ?? throw new InvalidOperationException($"Missing zip entry '{entryName}'.");
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<JsonElement>(stream);
    }

    private static void SetAmbient(IServiceScope scope, Guid workspaceId, Guid userId)
        => scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>().Set(new WorkspaceContext(
            workspaceId: workspaceId,
            userId: userId,
            membershipId: null,
            role: "Owner",
            permissions: new HashSet<string>(),
            entitlements: new HashSet<string>(),
            correlationId: Guid.NewGuid().ToString()));
}
