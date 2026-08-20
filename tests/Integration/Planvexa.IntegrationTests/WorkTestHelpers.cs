namespace Planvexa.IntegrationTests;

using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

// Response shapes for the work-management endpoints.
internal sealed record SpaceResp(Guid Id, string Name, double Position, bool IsArchived);
internal sealed record ListResp(Guid Id, Guid SpaceId, Guid? FolderId, string Name, Guid StatusSchemeId, double Position);
internal sealed record StatusResp(Guid Id, string Name, string Category, string Color, double Position, List<Guid> AllowedNextStatusIds);
internal sealed record SchemeResp(Guid Id, string Name, bool IsDefault, List<StatusResp> Statuses, Guid? SpaceId);
internal sealed record TaskResp(
    Guid Id, Guid ListId, Guid SpaceId, Guid? ParentId, long Sequence, string Title,
    Guid StatusId, string Priority, DateTimeOffset? DueDate, bool IsMilestone, bool IsCompleted,
    double Position, List<Guid> AssigneeUserIds, List<Guid> TagIds);
internal sealed record DependencyResp(Guid Id, Guid DependsOnTaskId, string Type);
internal sealed record RecurringResp(Guid Id, Guid ListId, string Title, string Frequency, int Interval, string TimeZoneId, DateTimeOffset NextRunUtc, bool IsActive);
internal sealed record GenResp(Guid DefinitionId, bool Generated, Guid? TaskId);
internal sealed record CustomFieldResp(Guid Id, string Name, string Type, string Scope, Guid? ScopeId, bool IsRequired);

/// <summary>Helpers to set up a workspace and drive the work-management API in tests.</summary>
internal static class WorkTestHelpers
{
    /// <summary>Registers a workspace and returns an authenticated client scoped to that workspace.</summary>
    public static async Task<(HttpClient Client, Guid WorkspaceId, string Slug, string Subject)> NewWorkspaceClientAsync(
        this PlanvexaFixture fixture)
    {
        var subject = TestData.NewSubject();
        var slug = TestData.NewSlug("wm");
        var bootstrap = fixture.AuthClient(subject);
        var response = await bootstrap.PostAsJsonAsync("/api/v1/workspaces", new { name = slug, slug });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceResponse>())!;

        var client = fixture.WorkClient(subject, workspace.Id);
        return (client, workspace.Id, workspace.Slug, subject);
    }

    public static HttpClient WorkClient(this PlanvexaFixture fixture, string subject, Guid workspaceId)
    {
        var client = fixture.AuthClient(subject);
        client.DefaultRequestHeaders.Add("X-Workspace", workspaceId.ToString());
        return client;
    }

    public static HttpClient WorkClient(this PlanvexaFixture fixture, string subject, string slug, Guid workspaceId)
    {
        _ = slug;
        return fixture.WorkClient(subject, workspaceId);
    }

    public static async Task<SpaceResp> CreateSpaceAsync(this HttpClient client, string name = "Engineering")
        => await ReadAsync<SpaceResp>(await client.PostAsJsonAsync("/api/v1/spaces", new { name }));

    public static async Task<FolderResp> CreateFolderAsync(this HttpClient client, Guid spaceId, string name = "Folder", Guid? parentFolderId = null)
        => await ReadAsync<FolderResp>(await client.PostAsJsonAsync($"/api/v1/spaces/{spaceId}/folders", new { name, parentFolderId }));

    public static async Task<ListResp> CreateListAsync(this HttpClient client, Guid spaceId, string name = "Sprint 1", Guid? folderId = null)
        => await ReadAsync<ListResp>(await client.PostAsJsonAsync("/api/v1/lists", new { spaceId, folderId, name }));

    public static async Task<TaskResp> CreateTaskAsync(this HttpClient client, Guid listId, string title = "Task", Guid? parentId = null)
        => await ReadAsync<TaskResp>(await client.PostAsJsonAsync("/api/v1/tasks", new { listId, title, parentId }));

    /// <summary>
    /// Deserializes a successful response, or throws with the status AND the problem-details body.
    /// <c>EnsureSuccessStatusCode</c> discards the body, which makes an occasional setup failure in
    /// these helpers impossible to diagnose from a CI log.
    /// </summary>
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{(int)response.StatusCode} from {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: "
                + await response.Content.ReadAsStringAsync());
        }

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static async Task<List<SchemeResp>> GetSchemesAsync(this HttpClient client)
        => (await client.GetFromJsonAsync<List<SchemeResp>>("/api/v1/status-schemes"))!;

    public static async Task<Guid> CurrentUserIdAsync(this HttpClient client)
        => (await client.GetFromJsonAsync<Planvexa.SharedContracts.Users.UserInfo>("/api/v1/users/me"))!.UserId;

    /// <summary>Ids of the spaces visible to the caller (ADR-0003 private-space filtering).</summary>
    public static async Task<List<Guid>> ListSpaceIdsAsync(this HttpClient client)
        => (await client.GetFromJsonAsync<List<SpaceResp>>("/api/v1/spaces"))!.Select(s => s.Id).ToList();

    /// <summary>Ids of the lists visible to the caller within a space (ADR-0003 private-list filtering).</summary>
    public static async Task<List<Guid>> ListListIdsAsync(this HttpClient client, Guid spaceId)
        => (await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{spaceId}/lists"))!.Select(l => l.Id).ToList();

    /// <summary>Invites a member to the workspace, accepts on a fresh subject, returns (subject, userId).</summary>
    public static async Task<(string Subject, Guid UserId)> InviteMemberAsync(
        this PlanvexaFixture fixture, HttpClient ownerClient, Guid workspaceId, string emailPrefix, string role = "Member")
    {
        var subject = TestData.NewSubject();
        var email = $"{subject}@planvexa.test";
        var inviteResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/invitations", new { email, role });
        inviteResponse.EnsureSuccessStatusCode();

        var token = fixture.LastInvitationToken(email)
            ?? throw new InvalidOperationException($"No invitation email was recorded for {email}.");

        var accept = await fixture.AuthClient(subject).PostAsync(
            new Uri($"/api/v1/invitations/{token}/accept", UriKind.Relative), null);
        accept.EnsureSuccessStatusCode();
        var accepted = await accept.Content.ReadFromJsonAsync<AcceptResponse>();

        var members = await ownerClient.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members");
        var userId = members!.Single(m => m.Id == accepted!.MembershipId).UserId;
        return (subject, userId);
    }

    public static string? LastInvitationToken(this PlanvexaFixture fixture, string email)
    {
        var log = fixture.Factory.Services.GetRequiredService<Planvexa.Api.Notifications.SentEmailLog>();
        var sent = log.ForEmail(email).LastOrDefault();
        if (sent is null)
        {
            return null;
        }

        const string marker = "/invite/";
        var idx = sent.Body.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var token = sent.Body[(idx + marker.Length)..];
        var end = token.IndexOfAny(['"', '\'', ' ', '?', '&', '\r', '\n']);
        return end >= 0 ? token[..end] : token;
    }
}


