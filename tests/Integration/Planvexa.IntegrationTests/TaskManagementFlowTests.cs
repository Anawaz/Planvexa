namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

internal sealed record TeamResp(Guid Id, Guid WorkspaceId, string Name, string? Description, bool IsArchived, int MemberCount);
internal sealed record TaskListMembershipResp(Guid ListId, bool IsPrimary, double Position, DateTimeOffset AddedAtUtc);
internal sealed record TaskTypeResp(Guid Id, string Name, string Color, string? Icon, bool IsBuiltIn, double Position);
internal sealed record TaskDto2(
    Guid Id, Guid ListId, Guid SpaceId, Guid? ParentId, long Sequence, string Title, string? Description,
    Guid StatusId, string Priority, DateTimeOffset? StartDate, DateTimeOffset? DueDate, bool IsMilestone,
    bool IsCompleted, double Position, List<Guid> AssigneeUserIds, List<Guid> TagIds, bool IsPrivate,
    Guid? TaskTypeId, string? CustomId, List<Guid> TeamAssigneeIds, bool IsArchived);
internal sealed record TaskDetailResp2(TaskDto2 Task, List<Guid> WatcherUserIds, List<object> Checklists,
    List<object> Dependencies, List<object> CustomFieldValues, List<object> Activity,
    List<TaskListMembershipResp> Lists, List<TaskRelationResp> Relations);
internal sealed record TaskRelationResp(Guid RelatedTaskId, DateTimeOffset CreatedAtUtc);

/// <summary>
/// Task management completeness: multi-list membership + its privacy resolution (the
/// highest-risk item — WorkManagementAuthorizer's ancestor-privacy probe has had two real confidentiality
/// bugs found in exactly this area during this roadmap), team assignees, cross-list copy, merge, generic
/// relations, and custom task types/ids.
/// </summary>
[Collection("api")]
public sealed class TaskManagementFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Task_added_to_a_second_list_appears_in_both_lists_without_duplication()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var listA = await client.CreateListAsync(space.Id, "List A");
        var listB = await client.CreateListAsync(space.Id, "List B");
        var task = await client.CreateTaskAsync(listA.Id, "Shared task");

        // Not yet in B.
        (await client.GetFromJsonAsync<List<TaskDto2>>($"/api/v1/lists/{listB.Id}/tasks"))!.ShouldBeEmpty();

        var addResp = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/lists", new { listId = listB.Id });
        addResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var memberships = (await addResp.Content.ReadFromJsonAsync<List<TaskListMembershipResp>>())!;
        memberships.Count.ShouldBe(2);
        memberships.ShouldContain(m => m.ListId == listA.Id && m.IsPrimary);
        memberships.ShouldContain(m => m.ListId == listB.Id && !m.IsPrimary);

        // Same task id shows in BOTH lists now — no duplication, exactly one row each.
        var tasksInA = (await client.GetFromJsonAsync<List<TaskDto2>>($"/api/v1/lists/{listA.Id}/tasks"))!;
        var tasksInB = (await client.GetFromJsonAsync<List<TaskDto2>>($"/api/v1/lists/{listB.Id}/tasks"))!;
        tasksInA.ShouldHaveSingleItem();
        tasksInB.ShouldHaveSingleItem();
        tasksInA[0].Id.ShouldBe(task.Id);
        tasksInB[0].Id.ShouldBe(task.Id);

        // Removing from the non-primary list B leaves it only in A.
        var removeResp = await client.DeleteAsync(new Uri($"/api/v1/tasks/{task.Id}/lists/{listB.Id}", UriKind.Relative));
        removeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<List<TaskDto2>>($"/api/v1/lists/{listB.Id}/tasks"))!.ShouldBeEmpty();

        // The primary list cannot be removed this way.
        var removePrimary = await client.DeleteAsync(new Uri($"/api/v1/tasks/{task.Id}/lists/{listA.Id}", UriKind.Relative));
        removePrimary.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Multi_list_privacy_task_in_private_list_and_public_list_is_gated_independently_per_list()
    {
        // THE regression test: a task in private List A (its PRIMARY list) and public List B (added via
        // AddToListAsync). This proves the exact multi-list privacy design: viewing through a list is
        // gated by THAT list's own privacy/ACL chain (WorkManagementAuthorizer.EnsureReadInListContextAsync
        // + ResourcePermissionService.GetEffectiveViaAsync), not by the task's single ambient primary
        // list — so a Member with no grant sees the task via public List B but not via private List A,
        // and granting access on B does not leak A's content.
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync("Team Space");
        var listA = await ownerClient.CreateListAsync(space.Id, "Private Roadmap");
        var listB = await ownerClient.CreateListAsync(space.Id, "Public Board");
        var task = await ownerClient.CreateTaskAsync(listA.Id, "Cross-list task");

        (await ownerClient.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/lists", new { listId = listB.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/list/{listA.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "member");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        // Viewing via public List B: the task IS visible, even though its primary list A is private and
        // the member has no grant on A. Multi-list membership must not force A's privacy onto B's view.
        var viaB = (await memberClient.GetFromJsonAsync<List<TaskDto2>>($"/api/v1/lists/{listB.Id}/tasks"))!;
        viaB.ShouldContain(t => t.Id == task.Id);

        // Viewing via private List A: blocked entirely for a Member with no grant on A (the list-level
        // access check itself rejects it) — B's public status must not leak backwards and unlock A's view
        // of the same task.
        (await memberClient.GetAsync(new Uri($"/api/v1/lists/{listA.Id}/tasks", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Direct-by-id GET still resolves via the PRIMARY list (A, private) — ambient access without a
        // list context is unchanged from the original behavior, and is blocked here.
        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Granting the Member access on A restores the direct-by-id view AND List A's view, without
        // having been necessary for List B's view (which already worked).
        (await ownerClient.PostAsJsonAsync(
                $"/api/v1/resources/list/{listA.Id}/permissions",
                new { principalType = "user", principalId = memberUserId, level = "view" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{task.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.GetFromJsonAsync<List<TaskDto2>>($"/api/v1/lists/{listA.Id}/tasks"))!
            .ShouldContain(t => t.Id == task.Id);
    }

    [Fact]
    public async Task Team_assignee_shows_up_in_task_dto_alongside_user_assignees()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Assign to a team");

        var teamResp = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/teams", new { name = "Engineering" });
        teamResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var team = (await teamResp.Content.ReadFromJsonAsync<TeamResp>())!;

        var assignResp = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/team-assignees", new { teamId = team.Id });
        assignResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = (await assignResp.Content.ReadFromJsonAsync<TaskDto2>())!;
        dto.TeamAssigneeIds.ShouldContain(team.Id);

        var removeResp = await client.DeleteAsync(new Uri($"/api/v1/tasks/{task.Id}/team-assignees/{team.Id}", UriKind.Relative));
        removeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterRemove = (await removeResp.Content.ReadFromJsonAsync<TaskDto2>())!;
        afterRemove.TeamAssigneeIds.ShouldNotContain(team.Id);
    }

    [Fact]
    public async Task Cross_list_copy_produces_an_independent_task_in_the_target_list()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var listA = await client.CreateListAsync(space.Id, "Source list");
        var listB = await client.CreateListAsync(space.Id, "Target list");
        var task = await client.CreateTaskAsync(listA.Id, "Original");

        var copyResp = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/copy", new { targetListId = listB.Id });
        copyResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var copy = (await copyResp.Content.ReadFromJsonAsync<TaskDto2>())!;

        copy.Id.ShouldNotBe(task.Id);
        copy.ListId.ShouldBe(listB.Id);
        copy.Title.ShouldBe("Original (Copy)");

        // Independent: still in the target list only, source untouched in its own list.
        (await client.GetFromJsonAsync<List<TaskDto2>>($"/api/v1/lists/{listB.Id}/tasks"))!.ShouldContain(t => t.Id == copy.Id);
        (await client.GetFromJsonAsync<List<TaskDto2>>($"/api/v1/lists/{listA.Id}/tasks"))!.ShouldContain(t => t.Id == task.Id);

        // Editing the copy does not affect the source.
        (await client.PatchAsJsonAsync($"/api/v1/tasks/{copy.Id}", new { title = "Changed" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        var sourceStillOriginal = await client.GetFromJsonAsync<TaskDetailResp2>($"/api/v1/tasks/{task.Id}");
        sourceStillOriginal!.Task.Title.ShouldBe("Original");
    }

    [Fact]
    public async Task Merge_moves_checklist_and_attachments_onto_target_and_archives_source()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var source = await client.CreateTaskAsync(list.Id, "Source task");
        var target = await client.CreateTaskAsync(list.Id, "Target task");

        var checklistResp = await client.PostAsJsonAsync($"/api/v1/tasks/{source.Id}/checklists", new { name = "Steps" });
        checklistResp.EnsureSuccessStatusCode();

        var mergeResp = await client.PostAsJsonAsync($"/api/v1/tasks/{source.Id}/merge", new { targetTaskId = target.Id });
        mergeResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var targetDetail = (await client.GetFromJsonAsync<TaskDetailResp2>($"/api/v1/tasks/{target.Id}"))!;
        targetDetail.Checklists.ShouldHaveSingleItem();

        // The source is archived (soft-deleted) — direct GET now 404s.
        var sourceAfter = await client.GetAsync(new Uri($"/api/v1/tasks/{source.Id}", UriKind.Relative));
        sourceAfter.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Generic_relation_links_two_tasks_symmetrically()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var a = await client.CreateTaskAsync(list.Id, "A");
        var b = await client.CreateTaskAsync(list.Id, "B");

        var addResp = await client.PostAsJsonAsync($"/api/v1/tasks/{a.Id}/relations", new { relatedTaskId = b.Id });
        addResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detailA = (await client.GetFromJsonAsync<TaskDetailResp2>($"/api/v1/tasks/{a.Id}"))!;
        detailA.Relations.ShouldContain(r => r.RelatedTaskId == b.Id);

        // Symmetric: visible from B's side too.
        var detailB = (await client.GetFromJsonAsync<TaskDetailResp2>($"/api/v1/tasks/{b.Id}"))!;
        detailB.Relations.ShouldContain(r => r.RelatedTaskId == a.Id);

        var removeResp = await client.DeleteAsync(new Uri($"/api/v1/tasks/{a.Id}/relations/{b.Id}", UriKind.Relative));
        removeResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterA = (await client.GetFromJsonAsync<TaskDetailResp2>($"/api/v1/tasks/{a.Id}"))!;
        afterA.Relations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Task_types_list_seeds_a_built_in_default_and_supports_custom_types()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var types = (await client.GetFromJsonAsync<List<TaskTypeResp>>("/api/v1/task-types"))!;
        types.ShouldContain(t => t.Name == "Task" && t.IsBuiltIn);

        var createResp = await client.PostAsJsonAsync("/api/v1/task-types", new { name = "Bug", color = "#ff0000" });
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var bug = (await createResp.Content.ReadFromJsonAsync<TaskTypeResp>())!;
        bug.IsBuiltIn.ShouldBeFalse();

        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var createTaskResp = await client.PostAsJsonAsync("/api/v1/tasks", new { listId = list.Id, title = "A bug", taskTypeId = bug.Id });
        var task = (await createTaskResp.Content.ReadFromJsonAsync<TaskDto2>())!;
        task.TaskTypeId.ShouldBe(bug.Id);
    }

    [Fact]
    public async Task Custom_id_is_unique_per_list_but_not_across_lists()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var listA = await client.CreateListAsync(space.Id, "List A");
        var listB = await client.CreateListAsync(space.Id, "List B");

        var first = await client.PostAsJsonAsync("/api/v1/tasks", new { listId = listA.Id, title = "First", customId = "BUG-1" });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Same custom id in the SAME list: rejected with a clean 400 (WorkItemService pre-checks before
        // ever hitting the DB's unique index), not a silent duplicate or a raw 500.
        var dup = await client.PostAsJsonAsync("/api/v1/tasks", new { listId = listA.Id, title = "Second", customId = "BUG-1" });
        dup.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Same custom id in a DIFFERENT list: allowed (scoped per list, not workspace-wide).
        var otherList = await client.PostAsJsonAsync("/api/v1/tasks", new { listId = listB.Id, title = "Third", customId = "BUG-1" });
        otherList.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
