namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

[Collection("api")]
public sealed class WorkManagementFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Full_hierarchy_and_task_lifecycle()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var space = await client.CreateSpaceAsync("Product");
        var list = await client.CreateListAsync(space.Id, "Backlog");
        list.StatusSchemeId.ShouldNotBe(Guid.Empty);

        var task = await client.CreateTaskAsync(list.Id, "Ship onboarding");
        task.Sequence.ShouldBe(1);
        task.IsCompleted.ShouldBeFalse();

        // Subtask shares the list.
        var subtask = await client.CreateTaskAsync(list.Id, "Design screens", task.Id);
        subtask.ParentId.ShouldBe(task.Id);
        subtask.Sequence.ShouldBe(2);

        // List view returns both tasks ordered by position.
        var tasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks");
        tasks!.Count.ShouldBe(2);

        // Complete the task.
        var completeResponse = await client.PostAsync(new Uri($"/api/v1/tasks/{task.Id}/complete", UriKind.Relative), null);
        completeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var completed = await completeResponse.Content.ReadFromJsonAsync<TaskResp>();
        completed!.IsCompleted.ShouldBeTrue();

        // Detail includes an activity feed.
        var detail = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/tasks/{task.Id}");
        detail.GetProperty("activity").GetArrayLength().ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Offline-mutation-outbox replay guard: a task create replayed with the same Idempotency-Key header
    /// (e.g. after the client never saw the first response) must return the ORIGINAL task, not insert a
    /// second row — see WorkItemService.CreateAsync's idempotency check.
    /// </summary>
    [Fact]
    public async Task Repeated_task_create_with_the_same_idempotency_key_does_not_duplicate()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var idempotencyKey = Guid.NewGuid().ToString();

        async Task<TaskResp> CreateWithKeyAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks")
            {
                Content = JsonContent.Create(new { listId = list.Id, title = "Offline-created task" }),
            };
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            var response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<TaskResp>())!;
        }

        var first = await CreateWithKeyAsync();
        var replay = await CreateWithKeyAsync();

        replay.Id.ShouldBe(first.Id);

        var tasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks");
        tasks!.Count(t => t.Title == "Offline-created task").ShouldBe(1);
    }

    [Fact]
    public async Task Dependency_blocks_completion_until_blocker_done()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var blocker = await client.CreateTaskAsync(list.Id, "Blocker");
        var blocked = await client.CreateTaskAsync(list.Id, "Blocked");

        // blocked is BlockedBy blocker.
        var depResponse = await client.PostAsJsonAsync($"/api/v1/tasks/{blocked.Id}/dependencies",
            new { dependsOnTaskId = blocker.Id, type = "BlockedBy" });
        depResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Cannot complete while blocker is open.
        var attempt = await client.PostAsync(new Uri($"/api/v1/tasks/{blocked.Id}/complete", UriKind.Relative), null);
        attempt.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Complete the blocker, then the blocked task completes.
        (await client.PostAsync(new Uri($"/api/v1/tasks/{blocker.Id}/complete", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsync(new Uri($"/api/v1/tasks/{blocked.Id}/complete", UriKind.Relative), null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Move_task_to_a_status_updates_completion_state()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Board card");

        var schemes = await client.GetSchemesAsync();
        var scheme = schemes.Single(s => s.Id == list.StatusSchemeId);
        var doneStatus = scheme.Statuses.First(s => s.Category == "Done");

        var moveResponse = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/move",
            new { statusId = doneStatus.Id });
        moveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var moved = await moveResponse.Content.ReadFromJsonAsync<TaskResp>();
        moved!.StatusId.ShouldBe(doneStatus.Id);
        moved.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Custom_field_value_is_typed_and_validated()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "With custom field");

        var fieldResponse = await client.PostAsJsonAsync("/api/v1/custom-fields",
            new { name = "Story Points", type = "Number", scope = "Workspace", isRequired = false });
        fieldResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var field = await fieldResponse.Content.ReadFromJsonAsync<CustomFieldResp>();

        // Valid number.
        var ok = await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field!.Id}", new { value = "8" });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Invalid number => validation error.
        var bad = await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = "not-a-number" });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recurring_generation_is_idempotent()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var createResponse = await client.PostAsJsonAsync("/api/v1/recurring-tasks", new
        {
            listId = list.Id,
            title = "Weekly report",
            frequency = "Weekly",
            interval = 1,
            timeZoneId = "UTC",
            anchorUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        });
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var recurring = await createResponse.Content.ReadFromJsonAsync<RecurringResp>();

        // First run generates a task.
        var run1 = await client.PostAsync(new Uri($"/api/v1/recurring-tasks/{recurring!.Id}/run", UriKind.Relative), null);
        var gen1 = await run1.Content.ReadFromJsonAsync<GenResp>();
        gen1!.Generated.ShouldBeTrue();
        gen1.TaskId.ShouldNotBeNull();

        // Immediate second run for the SAME occurrence must not generate a duplicate.
        var run2 = await client.PostAsync(new Uri($"/api/v1/recurring-tasks/{recurring.Id}/run", UriKind.Relative), null);
        var gen2 = await run2.Content.ReadFromJsonAsync<GenResp>();
        gen2!.Generated.ShouldBeFalse();

        // Exactly one task exists in the list.
        var tasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks");
        tasks!.Count(t => t.Title == "Weekly report").ShouldBe(1);
    }

    [Fact]
    public async Task Bulk_update_changes_multiple_tasks()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var t1 = await client.CreateTaskAsync(list.Id, "A");
        var t2 = await client.CreateTaskAsync(list.Id, "B");

        var due = DateTimeOffset.UtcNow.AddDays(3);
        var response = await client.PostAsJsonAsync("/api/v1/tasks/bulk", new
        {
            taskIds = new[] { t1.Id, t2.Id },
            dueDate = due,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("affected").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task My_work_returns_only_the_callers_assigned_tasks()
    {
        var (client, workspaceId, _, subject) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        // Resolve the caller's user id via /organizations/me is not exposed; assign via self.
        var me = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/tasks/mine");
        me.GetArrayLength().ShouldBe(0);

        var task = await client.CreateTaskAsync(list.Id, "Mine");
        // Add the current user as assignee: we need the user id. Fetch it from workspace members.
        var members = await client.GetFromJsonAsync<List<MemberResponse>>(
            $"/api/v1/workspaces/{workspaceId}/members");
        var myUserId = members!.Single().UserId;

        (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/assignees", new { userId = myUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var mine = await client.GetFromJsonAsync<List<TaskResp>>("/api/v1/tasks/mine");
        mine!.ShouldContain(t => t.Id == task.Id);
    }

    /// <summary>My Work "Created by me" section (product spec section 15): GET /tasks/mine?scope=created
    /// returns tasks the caller created, whether or not they are currently assigned — and the default
    /// (assigned) scope excludes an unassigned task the caller merely created.</summary>
    [Fact]
    public async Task Mine_with_created_scope_returns_tasks_the_caller_created_even_if_unassigned()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var task = await client.CreateTaskAsync(list.Id, "Created by me, unassigned");

        var createdByMe = await client.GetFromJsonAsync<List<TaskResp>>("/api/v1/tasks/mine?scope=created");
        createdByMe!.ShouldContain(t => t.Id == task.Id);

        // Not assigned to anyone, so the default "assigned to me" scope must not include it.
        var assignedToMe = await client.GetFromJsonAsync<List<TaskResp>>("/api/v1/tasks/mine");
        assignedToMe!.ShouldNotContain(t => t.Id == task.Id);

        // A second, unrelated workspace's caller must never see it via the created scope.
        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var otherCreatedByMe = await otherClient.GetFromJsonAsync<List<TaskResp>>("/api/v1/tasks/mine?scope=created");
        otherCreatedByMe!.ShouldNotContain(t => t.Id == task.Id);
    }

    /// <summary>My Work "Watching" section (product spec section 15): GET /tasks/mine?scope=watching
    /// returns tasks the caller watches, whether or not they are assigned or the author — and a
    /// cross-workspace caller must never see it.</summary>
    [Fact]
    public async Task Mine_with_watching_scope_returns_tasks_the_caller_watches()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Watched but unassigned");

        var watching = await client.GetFromJsonAsync<List<TaskResp>>("/api/v1/tasks/mine?scope=watching");
        watching!.ShouldNotContain(t => t.Id == task.Id);

        var members = await client.GetFromJsonAsync<List<MemberResponse>>(
            $"/api/v1/workspaces/{workspaceId}/members");
        var myUserId = members!.Single().UserId;

        (await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/watchers", new { userId = myUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        watching = await client.GetFromJsonAsync<List<TaskResp>>("/api/v1/tasks/mine?scope=watching");
        watching!.ShouldContain(t => t.Id == task.Id);

        // Not assigned to anyone, so the default "assigned to me" scope must not include it.
        var assignedToMe = await client.GetFromJsonAsync<List<TaskResp>>("/api/v1/tasks/mine");
        assignedToMe!.ShouldNotContain(t => t.Id == task.Id);

        // A second, unrelated workspace's caller must never see it via the watching scope.
        var (otherClient, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var otherWatching = await otherClient.GetFromJsonAsync<List<TaskResp>>("/api/v1/tasks/mine?scope=watching");
        otherWatching!.ShouldNotContain(t => t.Id == task.Id);
    }

    /// <summary>My Work workspace filter (product spec section 15): GET /tasks/mine?workspaceId=
    /// explicitly scopes "assigned to me" to one Workspace, for a caller who belongs to more than one.
    /// Row-Level Security (keyed on the caller's ambient X-Workspace header) is the actual authority —
    /// the query-string workspaceId can never widen access beyond it: pairing it with the matching
    /// X-Workspace header (the real My Work UI flow — see listMyTasks in apps/web/src/lib/work/client.ts)
    /// returns that Workspace's tasks, but pairing it with a DIFFERENT Workspace's header yields nothing,
    /// never that other Workspace's tasks — a spoofed workspaceId can never leak across Workspaces.</summary>
    [Fact]
    public async Task Mine_with_workspaceId_scopes_results_to_a_single_workspace_and_never_leaks_across_workspaces()
    {
        var (clientA, workspaceAId, _, subject) = await fixture.NewWorkspaceClientAsync();
        var spaceA = await clientA.CreateSpaceAsync();
        var listA = await clientA.CreateListAsync(spaceA.Id);
        var taskA = await clientA.CreateTaskAsync(listA.Id, "In workspace A");

        var membersA = await clientA.GetFromJsonAsync<List<MemberResponse>>(
            $"/api/v1/workspaces/{workspaceAId}/members");
        var myUserId = membersA!.Single().UserId;

        (await clientA.PostAsJsonAsync($"/api/v1/tasks/{taskA.Id}/assignees", new { userId = myUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Same underlying user, a second Workspace — as if they belong to both, like the workspace
        // switcher elsewhere in the app shell.
        var slugB = TestData.NewSlug("wm");
        var createB = await fixture.AuthClient(subject).PostAsJsonAsync("/api/v1/workspaces", new { name = slugB, slug = slugB });
        createB.EnsureSuccessStatusCode();
        var workspaceB = (await createB.Content.ReadFromJsonAsync<WorkspaceResponse>())!;
        var clientB = fixture.WorkClient(subject, workspaceB.Id);

        var spaceB = await clientB.CreateSpaceAsync();
        var listB = await clientB.CreateListAsync(spaceB.Id);
        var taskB = await clientB.CreateTaskAsync(listB.Id, "In workspace B");
        (await clientB.PostAsJsonAsync($"/api/v1/tasks/{taskB.Id}/assignees", new { userId = myUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Default (no filter) already only shows the caller's ambient Workspace's tasks.
        var defaultMineA = (await clientA.GetFromJsonAsync<List<TaskResp>>("/api/v1/tasks/mine"))!;
        defaultMineA.ShouldContain(t => t.Id == taskA.Id);
        defaultMineA.ShouldNotContain(t => t.Id == taskB.Id);

        // workspaceId matching the ambient Workspace: same result.
        var mineInA = (await clientA.GetFromJsonAsync<List<TaskResp>>($"/api/v1/tasks/mine?workspaceId={workspaceAId}"))!;
        mineInA.ShouldContain(t => t.Id == taskA.Id);
        mineInA.ShouldNotContain(t => t.Id == taskB.Id);

        // workspaceId for a DIFFERENT Workspace than the ambient one: never leaks that Workspace's
        // tasks, even though the caller is a legitimate member of it.
        var spoofedMineB = (await clientA.GetFromJsonAsync<List<TaskResp>>($"/api/v1/tasks/mine?workspaceId={workspaceB.Id}"))!;
        spoofedMineB.ShouldBeEmpty();

        // Switching the ambient Workspace to B (the real My Work "filter by workspace" flow) with a
        // matching workspaceId correctly returns Workspace B's tasks instead.
        var mineInB = (await clientB.GetFromJsonAsync<List<TaskResp>>($"/api/v1/tasks/mine?workspaceId={workspaceB.Id}"))!;
        mineInB.ShouldContain(t => t.Id == taskB.Id);
        mineInB.ShouldNotContain(t => t.Id == taskA.Id);

        // An unrelated Workspace the caller has no tasks in yields no results.
        var mineInUnrelated = (await clientA.GetFromJsonAsync<List<TaskResp>>($"/api/v1/tasks/mine?workspaceId={Guid.NewGuid()}"))!;
        mineInUnrelated.ShouldBeEmpty();
    }

    /// <summary>Assignment notification trigger (spec section 16): assigning a task to another workspace
    /// member fires an "assignment" notification for that member, deduplicated per task+assignee, and
    /// never fires when a user assigns the task to themselves.</summary>
    [Fact]
    public async Task Assigning_a_task_creates_an_assignment_notification_for_the_assignee_but_not_for_self_assignment()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (memberSubject, memberUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "assignee");
        var memberClient = fixture.WorkClient(memberSubject, slug, workspaceId);

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Please pick this up");

        (await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/assignees", new { userId = memberUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var notifications = await memberClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications?unreadOnly=true");
        notifications!.ShouldContain(n => n.EventType == "assignment" && n.EntityId == task.Id);

        // Removing then re-adding the same assignee is deduplicated per task+assignee: still one notification.
        (await owner.DeleteAsync(new Uri($"/api/v1/tasks/{task.Id}/assignees/{memberUserId}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/assignees", new { userId = memberUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterReassign = await memberClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications");
        afterReassign!.Count(n => n.EventType == "assignment" && n.EntityId == task.Id).ShouldBe(1);

        // Self-assignment never notifies: the owner assigning the task to themselves gets nothing.
        var members = await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members");
        var ownerUserId = members!.Single(m => m.UserId != memberUserId).UserId;
        (await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/assignees", new { userId = ownerUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var ownerNotifications = await owner.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications");
        ownerNotifications!.ShouldNotContain(n => n.EventType == "assignment");
    }

    /// <summary>Status-change notification trigger (spec section 16): changing a task's status via the
    /// task-update endpoint fires a "status_change" notification for its watchers and assignees, never
    /// for whoever made the change, deduplicated per task+status-transition+recipient.</summary>
    [Fact]
    public async Task Changing_a_tasks_status_notifies_watchers_and_assignees_but_not_the_actor()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var (watcherSubject, watcherUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "watcher");
        var watcherClient = fixture.WorkClient(watcherSubject, slug, workspaceId);

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Watched task");

        (await owner.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/watchers", new { userId = watcherUserId }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var scheme = (await owner.GetSchemesAsync()).Single(s => s.IsDefault);
        var toDo = scheme.Statuses.Single(s => s.Category == "NotStarted");
        var inProgress = scheme.Statuses.First(s => s.Category == "Active");
        var done = scheme.Statuses.Single(s => s.Category == "Done");

        (await owner.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = inProgress.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var notifications = await watcherClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications?unreadOnly=true");
        notifications!.ShouldContain(n => n.EventType == "status_change" && n.EntityId == task.Id);

        // The owner made the change themselves: no self-notification.
        var ownerNotifications = await owner.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications");
        ownerNotifications!.ShouldNotContain(n => n.EventType == "status_change");

        // Two more distinct transitions each notify again...
        (await owner.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = done.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = toDo.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // ...but repeating the very first transition (To Do -> In Progress) again is deduplicated per
        // task+transition+recipient, so it does not add a fourth notification.
        (await owner.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { statusId = inProgress.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterRepeat = await watcherClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications");
        afterRepeat!.Count(n => n.EventType == "status_change" && n.EntityId == task.Id).ShouldBe(3);
    }

    /// <summary>Activity history (spec section 18) must record Priority changed, Dates changed and
    /// task-type/custom-id changes distinctly, not just the generic status_changed/updated events — and
    /// only when the value actually changes (a no-op re-PATCH of the same priority adds no duplicate).</summary>
    [Fact]
    public async Task Updating_priority_dates_type_and_custom_id_each_write_a_distinct_activity_event()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Track activity");

        var typeResponse = await client.PostAsJsonAsync("/api/v1/task-types", new { name = "Bug" });
        typeResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var taskType = await typeResponse.Content.ReadFromJsonAsync<TaskTypeResp>();

        (await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new
        {
            priority = "Urgent",
            dueDate = DateTimeOffset.UtcNow.AddDays(2),
            taskTypeId = taskType!.Id,
            customId = "BUG-1",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        async Task<List<string?>> ActivityTypesAsync()
        {
            var events = await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>($"/api/v1/tasks/{task.Id}/activity");
            return events!.Select(e => e.GetProperty("type").GetString()).ToList();
        }

        var types = await ActivityTypesAsync();
        types.ShouldContain("priority_changed");
        types.ShouldContain("dates_changed");
        types.ShouldContain("task_type_changed");
        types.ShouldContain("custom_id_changed");

        // Re-applying the SAME priority is a no-op for that field: no duplicate event.
        var priorityEventsBefore = types.Count(t => t == "priority_changed");
        (await client.PatchAsJsonAsync($"/api/v1/tasks/{task.Id}", new { priority = "Urgent" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ActivityTypesAsync()).Count(t => t == "priority_changed").ShouldBe(priorityEventsBefore);
    }

    /// <summary>Activity history must also record a Custom Field changed event (CustomFieldService.SetValueAsync)
    /// and a dependency_removed event distinct from dependency_added (DependencyService.RemoveAsync) — both
    /// previously missing (spec section 18).</summary>
    [Fact]
    public async Task Custom_field_set_and_dependency_removal_write_activity_events()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "With activity");
        var other = await client.CreateTaskAsync(list.Id, "Blocker");

        var fieldResponse = await client.PostAsJsonAsync("/api/v1/custom-fields",
            new { name = "Story Points", type = "Number", scope = "Workspace", isRequired = false });
        fieldResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var field = await fieldResponse.Content.ReadFromJsonAsync<CustomFieldResp>();

        (await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field!.Id}", new { value = "8" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var depResponse = await client.PostAsJsonAsync($"/api/v1/tasks/{task.Id}/dependencies",
            new { dependsOnTaskId = other.Id, type = "BlockedBy" });
        depResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dependency = await depResponse.Content.ReadFromJsonAsync<DependencyResp>();

        (await client.DeleteAsync(new Uri($"/api/v1/tasks/{task.Id}/dependencies/{dependency!.Id}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var events = await client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>($"/api/v1/tasks/{task.Id}/activity");
        var types = events!.Select(e => e.GetProperty("type").GetString()).ToList();
        types.ShouldContain("custom_field_changed");
        types.ShouldContain("dependency_added");
        types.ShouldContain("dependency_removed");
    }
}
