namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

/// <summary>
/// Custom fields completeness: the new simple field types (User/Team/Phone/Location/Progress),
/// Relationship fields, Formula fields (parser/evaluator, cycle rejection, dependency recalculation) and
/// Rollup fields (subtask aggregation, permission-aware evaluation — the security-sensitive part, see the last test).
/// </summary>
[Collection("api")]
public sealed class CustomFieldsFlowTests(PlanvexaFixture fixture)
{
    private static async Task<JsonElement> TaskDetailAsync(HttpClient client, Guid taskId)
        => await client.GetFromJsonAsync<JsonElement>($"/api/v1/tasks/{taskId}");

    private static JsonElement? FindValue(JsonElement detail, Guid definitionId)
        => detail.GetProperty("customFieldValues").EnumerateArray()
            .Cast<JsonElement?>()
            .FirstOrDefault(v => v!.Value.GetProperty("definitionId").GetGuid() == definitionId);

    private static async Task<CustomFieldResp> CreateFieldAsync(HttpClient client, object payload)
    {
        var response = await client.PostAsJsonAsync("/api/v1/custom-fields", payload);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomFieldResp>())!;
    }

    [Fact]
    public async Task User_field_accepts_a_workspace_member_and_rejects_a_stranger()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id);
        var (_, memberUserId) = await fixture.InviteMemberAsync(client, workspaceId, "assignee");

        var field = await CreateFieldAsync(client, new { name = "Owner", type = "User", scope = "Workspace", isRequired = false });

        var ok = await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = memberUserId.ToString() });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);

        var stranger = await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = Guid.NewGuid().ToString() });
        stranger.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Team_field_stores_an_opaque_team_id()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id);

        var field = await CreateFieldAsync(client, new { name = "Owning team", type = "Team", scope = "Workspace", isRequired = false });
        var ok = await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = Guid.NewGuid().ToString() });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Phone_field_rejects_an_obviously_invalid_value()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id);

        var field = await CreateFieldAsync(client, new { name = "Phone", type = "Phone", scope = "Workspace", isRequired = false });

        var ok = await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = "+1 (415) 555-0100" });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);

        var bad = await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = "abc" });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Location_field_stores_a_free_text_address()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id);

        var field = await CreateFieldAsync(client, new { name = "Site", type = "Location", scope = "Workspace", isRequired = false });
        var ok = await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = "1 Infinite Loop, Cupertino, CA" });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await TaskDetailAsync(client, task.Id);
        FindValue(detail, field.Id)!.Value.GetProperty("text").GetString().ShouldBe("1 Infinite Loop, Cupertino, CA");
    }

    [Fact]
    public async Task Progress_field_is_bounded_to_0_through_100()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id);

        var field = await CreateFieldAsync(client, new { name = "Progress", type = "Progress", scope = "Workspace", isRequired = false });

        (await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = "42" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = "142" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{field.Id}", new { value = "-1" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Relationship_field_links_tasks_via_a_task_picker_and_is_workspace_scoped_not_list_scoped()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var listA = await client.CreateListAsync(space.Id, "List A");
        var listB = await client.CreateListAsync(space.Id, "List B");
        var epic = await client.CreateTaskAsync(listA.Id, "Epic");
        var deliverable = await client.CreateTaskAsync(listB.Id, "Deliverable");

        var field = await CreateFieldAsync(client, new { name = "Related Epic", type = "Relationship", scope = "Workspace", isRequired = false });

        var setResp = await client.PutAsJsonAsync(
            $"/api/v1/tasks/{deliverable.Id}/custom-fields/{field.Id}/relationships", new { relatedTaskIds = new[] { epic.Id } });
        setResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await TaskDetailAsync(client, deliverable.Id);
        var value = FindValue(detail, field.Id)!.Value;
        value.GetProperty("relatedTaskIds").EnumerateArray().Select(e => e.GetGuid()).ShouldContain(epic.Id);

        // Setting it directly through the plain value endpoint is rejected — must use /relationships.
        var wrongEndpoint = await client.PutAsJsonAsync($"/api/v1/tasks/{deliverable.Id}/custom-fields/{field.Id}", new { value = epic.Id.ToString() });
        wrongEndpoint.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Formula_field_computes_from_other_fields_and_reflects_dependency_changes_immediately()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id);

        var a = await CreateFieldAsync(client, new { name = "A", type = "Number", scope = "Workspace", isRequired = false });
        var b = await CreateFieldAsync(client, new { name = "B", type = "Number", scope = "Workspace", isRequired = false });
        var total = await CreateFieldAsync(client, new
        {
            name = "Total", type = "Formula", scope = "Workspace", isRequired = false, formulaExpression = "{A} + {B}",
        });

        await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{a.Id}", new { value = "3" });
        await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{b.Id}", new { value = "4" });

        var detail1 = await TaskDetailAsync(client, task.Id);
        FindValue(detail1, total.Id)!.Value.GetProperty("number").GetDecimal().ShouldBe(7);

        // Recalculation is event-driven off the task-update path (SetValueAsync), not a poll — the
        // very next read after A changes reflects it, with no separate "recompute" step to run.
        await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{a.Id}", new { value = "10" });
        var detail2 = await TaskDetailAsync(client, task.Id);
        FindValue(detail2, total.Id)!.Value.GetProperty("number").GetDecimal().ShouldBe(14);
    }

    [Fact]
    public async Task Formula_field_rejects_a_malformed_expression_at_save_time()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/custom-fields", new
        {
            name = "Broken", type = "Formula", scope = "Workspace", isRequired = false, formulaExpression = "1 +",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Formula_field_rejects_a_reference_to_an_unknown_field()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var response = await client.PostAsJsonAsync("/api/v1/custom-fields", new
        {
            name = "Broken", type = "Formula", scope = "Workspace", isRequired = false, formulaExpression = "{DoesNotExist} + 1",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A Formula field may legitimately reference another Formula field, forming a multi-level dependency
    /// chain — evaluated in topological order (see CustomFieldDependencyGraph) so the dependency is
    /// computed before the field that references it. A TRUE cycle (A depends on B depends on A) cannot be
    /// constructed through this create-only API at all — creating a field can only reference fields that
    /// already exist, so every reachable dependency graph is a DAG by construction; there is no Update
    /// endpoint for a field definition that could introduce a back-edge after the fact. Cycle REJECTION
    /// itself (CustomFieldDependencyGraph.HasCycle) is unit-tested directly at the pure graph level in
    /// CustomFieldsTests.CustomFieldDependencyGraphTests, which is the only way to exercise it today.
    /// </summary>
    [Fact]
    public async Task Formula_field_may_depend_on_another_formula_field()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id);

        var a = await CreateFieldAsync(client, new { name = "FieldA", type = "Number", scope = "Workspace", isRequired = false });
        var doubled = await CreateFieldAsync(client, new
        {
            name = "Doubled", type = "Formula", scope = "Workspace", isRequired = false, formulaExpression = "{FieldA} * 2",
        });
        var plusOne = await CreateFieldAsync(client, new
        {
            name = "PlusOne", type = "Formula", scope = "Workspace", isRequired = false, formulaExpression = "{Doubled} + 1",
        });

        await client.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/custom-fields/{a.Id}", new { value = "5" });

        var detail = await TaskDetailAsync(client, task.Id);
        FindValue(detail, doubled.Id)!.Value.GetProperty("number").GetDecimal().ShouldBe(10);
        FindValue(detail, plusOne.Id)!.Value.GetProperty("number").GetDecimal().ShouldBe(11);
    }

    [Fact]
    public async Task Rollup_sums_a_target_field_across_direct_subtasks_only()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var parent = await client.CreateTaskAsync(list.Id, "Parent");
        var childA = await client.CreateTaskAsync(list.Id, "Child A", parent.Id);
        var childB = await client.CreateTaskAsync(list.Id, "Child B", parent.Id);
        var grandchild = await client.CreateTaskAsync(list.Id, "Grandchild", childA.Id);

        var points = await CreateFieldAsync(client, new { name = "Points", type = "Number", scope = "Workspace", isRequired = false });
        var sumRollup = await CreateFieldAsync(client, new
        {
            name = "Total points", type = "Rollup", scope = "Workspace", isRequired = false,
            rollupSourceType = "Subtasks", rollupTargetFieldId = points.Id, rollupFunction = "Sum",
        });
        var countRollup = await CreateFieldAsync(client, new
        {
            name = "Subtask count", type = "Rollup", scope = "Workspace", isRequired = false,
            rollupSourceType = "Subtasks", rollupFunction = "Count",
        });

        await client.PutAsJsonAsync($"/api/v1/tasks/{childA.Id}/custom-fields/{points.Id}", new { value = "3" });
        await client.PutAsJsonAsync($"/api/v1/tasks/{childB.Id}/custom-fields/{points.Id}", new { value = "5" });
        // Grandchild is NOT a direct subtask of parent — must not be double-counted or summed in.
        await client.PutAsJsonAsync($"/api/v1/tasks/{grandchild.Id}/custom-fields/{points.Id}", new { value = "100" });

        var detail = await TaskDetailAsync(client, parent.Id);
        FindValue(detail, sumRollup.Id)!.Value.GetProperty("number").GetDecimal().ShouldBe(8);
        FindValue(detail, countRollup.Id)!.Value.GetProperty("number").GetDecimal().ShouldBe(2);
    }

    /// <summary>
    /// Option (b): a Rollup's source tasks are filtered per-viewer through the same authorization
    /// check as everywhere else, so a Sum rollup must NOT reflect a value that came from a subtask the
    /// current viewer cannot otherwise browse to. This is the specific privacy-correctness proof this needs
    /// — exactly this kind of leak is the recurring risk in this area.
    /// </summary>
    [Fact]
    public async Task Rollup_does_not_leak_a_value_from_a_subtask_the_viewer_cannot_see()
    {
        var (ownerClient, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await ownerClient.CreateSpaceAsync();
        var list = await ownerClient.CreateListAsync(space.Id);
        var parent = await ownerClient.CreateTaskAsync(list.Id, "Parent");
        var visibleChild = await ownerClient.CreateTaskAsync(list.Id, "Visible child", parent.Id);
        var secretChild = await ownerClient.CreateTaskAsync(list.Id, "Secret child", parent.Id);

        var points = await CreateFieldAsync(ownerClient, new { name = "Points", type = "Number", scope = "Workspace", isRequired = false });
        var sumRollup = await CreateFieldAsync(ownerClient, new
        {
            name = "Total points", type = "Rollup", scope = "Workspace", isRequired = false,
            rollupSourceType = "Subtasks", rollupTargetFieldId = points.Id, rollupFunction = "Sum",
        });

        await ownerClient.PutAsJsonAsync($"/api/v1/tasks/{visibleChild.Id}/custom-fields/{points.Id}", new { value = "3" });
        await ownerClient.PutAsJsonAsync($"/api/v1/tasks/{secretChild.Id}/custom-fields/{points.Id}", new { value = "1000" });

        // Make the secret child private — only the owner (creator) and explicitly-granted principals can see it.
        (await ownerClient.PatchAsJsonAsync($"/api/v1/resources/task/{secretChild.Id}/private", new { isPrivate = true }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var (memberSubject, _) = await fixture.InviteMemberAsync(ownerClient, workspaceId, "viewer");
        var memberClient = fixture.WorkClient(memberSubject, workspaceId);

        // The Member can still read the parent task and the visible child...
        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{parent.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        // ...but not the private secret child directly.
        (await memberClient.GetAsync(new Uri($"/api/v1/tasks/{secretChild.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The rollup, read as the Member, must reflect ONLY the visible child (3) — NOT 1003.
        var memberView = await TaskDetailAsync(memberClient, parent.Id);
        FindValue(memberView, sumRollup.Id)!.Value.GetProperty("number").GetDecimal().ShouldBe(3);

        // The owner, who can see both children, gets the full aggregate.
        var ownerView = await TaskDetailAsync(ownerClient, parent.Id);
        FindValue(ownerView, sumRollup.Id)!.Value.GetProperty("number").GetDecimal().ShouldBe(1003);
    }
}
