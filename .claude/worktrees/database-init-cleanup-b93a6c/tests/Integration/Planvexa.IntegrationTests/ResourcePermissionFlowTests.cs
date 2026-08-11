namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

/// <summary>
/// ADR-0003: per-resource ACL end-to-end — private Space/List visibility, ACL grants, and RLS
/// isolation of tenancy.resource_permissions across workspaces.
/// </summary>
[Collection("api")]
public sealed class ResourcePermissionFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Private_space_is_invisible_to_a_member_without_an_explicit_grant()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Confidential");
        var (memberSubject, _) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        // Baseline: not yet private, the Member can see it.
        (await memberClient.ListSpaceIdsAsync()).ShouldContain(space.Id);

        var makePrivate = await ownerClient.PatchAsJsonAsync(
            $"/api/v1/resources/space/{space.Id}/private", new { isPrivate = true });
        makePrivate.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await memberClient.ListSpaceIdsAsync()).ShouldNotContain(space.Id);

        // The owner (Admin+, coarse role) still sees it — private only removes the coarse-role floor
        // for callers without a grant, it does not hide the resource from managers.
        (await ownerClient.ListSpaceIdsAsync()).ShouldContain(space.Id);
    }

    [Fact]
    public async Task Member_with_an_explicit_grant_can_see_a_private_space()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Confidential");
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/space/{space.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.ListSpaceIdsAsync()).ShouldNotContain(space.Id);

        var grant = await ownerClient.PostAsJsonAsync(
            $"/api/v1/resources/space/{space.Id}/permissions",
            new { principalType = "user", principalId = memberUserId, level = "view" });
        grant.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await memberClient.ListSpaceIdsAsync()).ShouldContain(space.Id);

        // Revoking the grant hides it again.
        var revoke = await ownerClient.DeleteAsync(
            new Uri($"/api/v1/resources/space/{space.Id}/permissions/user/{memberUserId}", UriKind.Relative));
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await memberClient.ListSpaceIdsAsync()).ShouldNotContain(space.Id);
    }

    [Fact]
    public async Task Private_list_inside_a_public_space_blocks_non_granted_members_while_the_space_stays_visible()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Team Space");
        var list = await ownerClient.CreateListAsync(space.Id, "Secret Roadmap");

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        // Baseline: the Member can see both.
        (await memberClient.ListSpaceIdsAsync()).ShouldContain(space.Id);
        (await memberClient.ListListIdsAsync(space.Id)).ShouldContain(list.Id);

        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/list/{list.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The space itself is still visible (it was never made private)...
        (await memberClient.ListSpaceIdsAsync()).ShouldContain(space.Id);
        // ...but the private list inside it is filtered out for a Member with no grant.
        (await memberClient.ListListIdsAsync(space.Id)).ShouldNotContain(list.Id);
        // Direct GET is blocked too, not just the listing.
        (await memberClient.GetAsync(new Uri($"/api/v1/lists/{list.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Granting the Member access on the list restores it, still without touching the space.
        (await ownerClient.PostAsJsonAsync(
                $"/api/v1/resources/list/{list.Id}/permissions",
                new { principalType = "user", principalId = memberUserId, level = "edit" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await memberClient.ListListIdsAsync(space.Id)).ShouldContain(list.Id);
    }

    [Fact]
    public async Task A_non_private_task_inside_a_private_list_is_blocked_from_direct_access_by_id()
    {
        // Regression: WorkManagementAuthorizer's cheap pre-filter used to only check the resource's OWN
        // IsPrivate/ACL rows, so a non-private Task inside a private List was readable by ANY Member via
        // GET /api/v1/tasks/{id} even with no grant anywhere. The ancestor-privacy probe must catch this.
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Team Space");
        var list = await ownerClient.CreateListAsync(space.Id, "Secret Roadmap");
        var task = await ownerClient.CreateTaskAsync(list.Id, "Not private itself");

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        // Baseline: visible before the list is made private.
        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/list/{list.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // The task itself carries no privacy flag or ACL row — direct-by-id access must still be blocked
        // because its containing List is now private.
        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A grant on the LIST (not the task) restores access to the task reached through it.
        (await ownerClient.PostAsJsonAsync(
                $"/api/v1/resources/list/{list.Id}/permissions",
                new { principalType = "user", principalId = memberUserId, level = "edit" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_non_private_list_inside_a_private_folder_is_blocked_from_direct_access_by_id()
    {
        // Same bug shape one level up: a private Folder must still gate a non-private List's own
        // direct-by-id GET, not just the folder's own listing.
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Team Space");
        var folder = await ownerClient.CreateFolderAsync(space.Id, "Secret Folder");
        var list = await ownerClient.CreateListAsync(space.Id, "Not private itself", folder.Id);

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        (await memberClient.GetAsync(new Uri($"/api/v1/lists/{list.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/folder/{folder.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await memberClient.GetAsync(new Uri($"/api/v1/lists/{list.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ownerClient.PostAsJsonAsync(
                $"/api/v1/resources/folder/{folder.Id}/permissions",
                new { principalType = "user", principalId = memberUserId, level = "edit" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await memberClient.GetAsync(new Uri($"/api/v1/lists/{list.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Private_folder_nested_two_levels_deep_blocks_direct_access_to_itself_and_its_descendants()
    {
        // Follow-up: folders now nest to arbitrary depth, so the ancestor-privacy probe
        // (WorkResourceHierarchyQuery + WorkManagementAuthorizer) must walk multiple Folder->Folder hops,
        // not just one Folder->Space hop. Chain: Space (public) -> Folder A (public) -> Folder B (private)
        // -> List (public) -> Task (public), 3+ levels deep from the Space.
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Team Space");
        var folderA = await ownerClient.CreateFolderAsync(space.Id, "Folder A");
        var folderB = await ownerClient.CreateFolderAsync(space.Id, "Folder B", folderA.Id);
        var list = await ownerClient.CreateListAsync(space.Id, "Not private itself", folderB.Id);
        var task = await ownerClient.CreateTaskAsync(list.Id, "Not private itself");

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        // Baseline: everything visible before Folder B is made private.
        (await memberClient.GetAsync(new Uri($"/api/v1/folders/{folderB.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.GetAsync(new Uri($"/api/v1/lists/{list.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/folder/{folderB.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A Member with no grant is blocked from Folder B itself, and from the non-private List and Task
        // reached only THROUGH it -- two Folder->Folder/List->Folder hops away, not one.
        (await memberClient.GetAsync(new Uri($"/api/v1/folders/{folderB.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await memberClient.GetAsync(new Uri($"/api/v1/lists/{list.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Folder A (the public ancestor above the private one) stays visible -- privacy does not leak upward.
        (await memberClient.GetAsync(new Uri($"/api/v1/folders/{folderA.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A grant on Folder B (not on the List or Task) restores access to all three.
        (await ownerClient.PostAsJsonAsync(
                $"/api/v1/resources/folder/{folderB.Id}/permissions",
                new { principalType = "user", principalId = memberUserId, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await memberClient.GetAsync(new Uri($"/api/v1/folders/{folderB.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.GetAsync(new Uri($"/api/v1/lists/{list.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resource_permissions_table_is_isolated_between_workspaces_by_row_level_security()
    {
        var (ownerA, workspaceA, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await ownerA.CreateSpaceAsync("A Space");
        var (_, userA) = await fixture.InviteMemberAsync(ownerA, workspaceA, "member-a");
        (await ownerA.PostAsJsonAsync(
                $"/api/v1/resources/space/{spaceA.Id}/permissions",
                new { principalType = "user", principalId = userA, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var (ownerB, workspaceB, _, _) = await fixture.NewWorkspaceClientAsync();
        var spaceB = await ownerB.CreateSpaceAsync("B Space");
        var (_, userB) = await fixture.InviteMemberAsync(ownerB, workspaceB, "member-b");
        (await ownerB.PostAsJsonAsync(
                $"/api/v1/resources/space/{spaceB.Id}/permissions",
                new { principalType = "user", principalId = userB, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Read workspace A's ACL rows directly through the non-superuser role with A's workspace GUC set.
        await using var connection = new Npgsql.NpgsqlConnection(fixture.AppRoleConnectionString);
        await connection.OpenAsync();
        await using (var setGuc = connection.CreateCommand())
        {
            setGuc.CommandText = "SELECT set_config('app.current_workspace', @workspace, false)";
            setGuc.Parameters.AddWithValue("workspace", workspaceA.ToString());
            await setGuc.ExecuteNonQueryAsync();
        }

        var visibleResourceIds = new List<Guid>();
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT resource_id FROM tenancy.resource_permissions";
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                visibleResourceIds.Add(reader.GetGuid(0));
            }
        }

        visibleResourceIds.ShouldContain(spaceA.Id);
        visibleResourceIds.ShouldNotContain(spaceB.Id);
    }
}
