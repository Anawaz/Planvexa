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
}
