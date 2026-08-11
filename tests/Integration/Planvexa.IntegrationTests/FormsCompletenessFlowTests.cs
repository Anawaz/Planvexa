namespace Planvexa.IntegrationTests;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shouldly;
using Xunit;

// Response shapes for forms completeness. TeamResp is already defined in TaskManagementFlowTests.cs.
// GET /api/v1/tasks/{id} returns an envelope ({ task, watcherUserIds, checklists, ... }), not a bare TaskDto.
internal sealed record TaskDetailResp(Guid Id, Guid StatusId, string Priority, DateTimeOffset? DueDate, List<Guid> TagIds, List<Guid> TeamAssigneeIds, List<Guid> AssigneeUserIds);
internal sealed record TaskDetailEnvelope(TaskDetailResp Task);

[Collection("api")]
public sealed class FormsCompletenessFlowTests(PlanvexaFixture fixture)
{
    private static async Task<T?> PollAsync<T>(Func<Task<T?>> probe, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(500);
        }

        return null;
    }

    [Fact]
    public async Task Full_routing_sets_status_priority_tags_due_date_and_team_on_the_created_task()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var schemes = await client.GetSchemesAsync();
        var targetStatus = schemes.Single(s => s.IsDefault).Statuses.OrderBy(s => s.Position).Last(); // a non-first status

        var createTeam = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/teams", new { name = "Support", description = (string?)null });
        createTeam.StatusCode.ShouldBe(HttpStatusCode.Created);
        var team = (await createTeam.Content.ReadFromJsonAsync<TeamResp>())!;

        var create = await client.PostAsJsonAsync("/api/v1/forms", new
        {
            listId = list.Id,
            title = "Routed intake",
            fields = new[] { new { label = "Summary", type = "Text", required = true, options = Array.Empty<string>(), position = 0 } },
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var form = (await create.Content.ReadFromJsonAsync<FormResp>())!;

        var settings = await client.PatchAsJsonAsync($"/api/v1/forms/{form.Id}/settings", new
        {
            targetStatusName = targetStatus.Name,
            targetPriority = "High",
            targetTagsCsv = "vip,routed",
            targetTeamId = team.Id,
            dueDateDaysAfterSubmission = 5,
        });
        settings.StatusCode.ShouldBe(HttpStatusCode.OK);

        var anon = fixture.Factory.CreateClient();
        var summaryFieldId = form.Fields.Single(f => f.Label == "Summary").Id.ToString();
        var submit = await anon.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}/submissions",
            new { values = new Dictionary<string, string> { [summaryFieldId] = "Route me" } });
        submit.EnsureSuccessStatusCode();
        var result = (await submit.Content.ReadFromJsonAsync<SubmitResultResp>())!;
        result.CreatedTaskId.ShouldNotBeNull();

        var envelope = await client.GetFromJsonAsync<TaskDetailEnvelope>($"/api/v1/tasks/{result.CreatedTaskId}");
        envelope.ShouldNotBeNull();
        var task = envelope!.Task;
        task.StatusId.ShouldBe(targetStatus.Id);
        task.Priority.ShouldBe("High");
        task.TeamAssigneeIds.ShouldContain(team.Id);
        task.DueDate.ShouldNotBeNull();
        task.DueDate!.Value.Date.ShouldBe(DateTimeOffset.UtcNow.AddDays(5).Date);

        var tags = await client.GetFromJsonAsync<List<TagResp>>("/api/v1/tags");
        var routedTagIds = tags!.Where(t => t.Name is "vip" or "routed").Select(t => t.Id).ToList();
        routedTagIds.Count.ShouldBe(2);
        task.TagIds.ShouldContain(routedTagIds[0]);
        task.TagIds.ShouldContain(routedTagIds[1]);
    }

    [Fact]
    public async Task Submission_assigns_the_created_task_to_the_forms_target_user()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var (_, assigneeUserId) = await fixture.InviteMemberAsync(client, workspaceId, "assignee");

        var create = await client.PostAsJsonAsync("/api/v1/forms", new
        {
            listId = list.Id,
            title = "Assigned intake",
            fields = new[] { new { label = "Summary", type = "Text", required = true, options = Array.Empty<string>(), position = 0 } },
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var form = (await create.Content.ReadFromJsonAsync<FormResp>())!;

        var settings = await client.PatchAsJsonAsync($"/api/v1/forms/{form.Id}/settings", new { targetUserId = assigneeUserId });
        settings.StatusCode.ShouldBe(HttpStatusCode.OK);

        var anon = fixture.Factory.CreateClient();
        var summaryFieldId = form.Fields.Single(f => f.Label == "Summary").Id.ToString();
        var submit = await anon.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}/submissions",
            new { values = new Dictionary<string, string> { [summaryFieldId] = "Assign me" } });
        submit.EnsureSuccessStatusCode();
        var result = (await submit.Content.ReadFromJsonAsync<SubmitResultResp>())!;
        result.CreatedTaskId.ShouldNotBeNull();

        var envelope = await client.GetFromJsonAsync<TaskDetailEnvelope>($"/api/v1/tasks/{result.CreatedTaskId}");
        envelope!.Task.AssigneeUserIds.ShouldContain(assigneeUserId);
    }

    [Fact]
    public async Task Submission_does_not_assign_a_target_user_from_a_different_workspace()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        // A user who is a member of a DIFFERENT workspace only — never invited into this one.
        var (_, outsiderWorkspaceId, _, outsiderSubject) = await fixture.NewWorkspaceClientAsync();
        _ = outsiderWorkspaceId;
        var outsiderUserId = await fixture.WorkClient(outsiderSubject, outsiderWorkspaceId).CurrentUserIdAsync();

        var create = await client.PostAsJsonAsync("/api/v1/forms", new
        {
            listId = list.Id,
            title = "Cross-workspace intake",
            fields = new[] { new { label = "Summary", type = "Text", required = true, options = Array.Empty<string>(), position = 0 } },
        });
        var form = (await create.Content.ReadFromJsonAsync<FormResp>())!;

        var settings = await client.PatchAsJsonAsync($"/api/v1/forms/{form.Id}/settings", new { targetUserId = outsiderUserId });
        settings.StatusCode.ShouldBe(HttpStatusCode.OK);

        var anon = fixture.Factory.CreateClient();
        var summaryFieldId = form.Fields.Single(f => f.Label == "Summary").Id.ToString();
        var submit = await anon.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}/submissions",
            new { values = new Dictionary<string, string> { [summaryFieldId] = "Do not assign" } });
        submit.EnsureSuccessStatusCode();
        var result = (await submit.Content.ReadFromJsonAsync<SubmitResultResp>())!;
        result.CreatedTaskId.ShouldNotBeNull();

        var envelope = await client.GetFromJsonAsync<TaskDetailEnvelope>($"/api/v1/tasks/{result.CreatedTaskId}");
        envelope!.Task.AssigneeUserIds.ShouldNotContain(outsiderUserId);
    }

    [Fact]
    public async Task Form_submission_triggers_an_automation_via_the_form_submitted_trigger()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var createRule = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Tag form submissions",
            triggerType = "form.submitted",
            conditionJson = "{}",
            actionJson = "[{\"type\":\"add_tag\",\"value\":\"from-form\"}]",
        });
        createRule.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = (await createRule.Content.ReadFromJsonAsync<AutomationRuleResp>())!;

        var create = await client.PostAsJsonAsync("/api/v1/forms", new
        {
            listId = list.Id,
            title = "Automated intake",
            fields = new[] { new { label = "Summary", type = "Text", required = true, options = Array.Empty<string>(), position = 0 } },
        });
        var form = (await create.Content.ReadFromJsonAsync<FormResp>())!;
        var fieldId = form.Fields.Single().Id.ToString();

        var anon = fixture.Factory.CreateClient();
        var submit = await anon.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}/submissions",
            new { values = new Dictionary<string, string> { [fieldId] = "Trigger the rule" } });
        submit.EnsureSuccessStatusCode();

        var runs = await PollAsync(async () =>
        {
            var found = await client.GetFromJsonAsync<List<AutomationRunResp>>($"/api/v1/automations/{rule.Id}/runs");
            return found is { Count: > 0 } ? found : null;
        }, TimeSpan.FromSeconds(40));

        runs.ShouldNotBeNull();
        runs!.ShouldContain(r => r.Status == "Success");

        var tags = await client.GetFromJsonAsync<List<TagResp>>("/api/v1/tags");
        tags!.ShouldContain(t => t.Name == "from-form");
    }

    [Fact]
    public async Task Submission_is_rejected_once_the_total_submission_limit_is_reached()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var create = await client.PostAsJsonAsync("/api/v1/forms", new
        {
            listId = list.Id,
            title = "Capped intake",
            fields = new[] { new { label = "Summary", type = "Text", required = true, options = Array.Empty<string>(), position = 0 } },
        });
        var form = (await create.Content.ReadFromJsonAsync<FormResp>())!;
        var fieldId = form.Fields.Single().Id.ToString();

        var settings = await client.PatchAsJsonAsync($"/api/v1/forms/{form.Id}/settings", new { maxTotalSubmissions = 1 });
        settings.StatusCode.ShouldBe(HttpStatusCode.OK);

        var anon = fixture.Factory.CreateClient();
        Task<HttpResponseMessage> Submit(string idempotencyKey)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/public/forms/{form.PublicToken}/submissions")
            {
                Content = JsonContent.Create(new { values = new Dictionary<string, string> { [fieldId] = "First" } }),
            };
            req.Headers.Add("Idempotency-Key", idempotencyKey);
            return anon.SendAsync(req);
        }

        var first = await Submit("cap-1");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await Submit("cap-2");
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var submissions = await client.GetFromJsonAsync<List<FormSubmissionResp>>($"/api/v1/forms/{form.Id}/submissions");
        submissions!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Form_builder_and_submissions_stay_workspace_permission_gated_though_the_form_is_publicly_submittable()
    {
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);

        var create = await owner.PostAsJsonAsync("/api/v1/forms", new
        {
            listId = list.Id,
            title = "Gated intake",
            fields = new[] { new { label = "Summary", type = "Text", required = true, options = Array.Empty<string>(), position = 0 } },
        });
        var form = (await create.Content.ReadFromJsonAsync<FormResp>())!;
        var fieldId = form.Fields.Single().Id.ToString();

        // Public submission succeeds anonymously — that part is meant to be open.
        var anon = fixture.Factory.CreateClient();
        var submit = await anon.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}/submissions",
            new { values = new Dictionary<string, string> { [fieldId] = "Visible to nobody but the owner" } });
        submit.EnsureSuccessStatusCode();

        // A workspace OUTSIDER (never invited) authenticates fine as a user, but has no membership in
        // THIS workspace — the form builder config and submission list/export must all reject them.
        var (_, _, _, outsiderSubject) = await fixture.NewWorkspaceClientAsync();
        var outsider = fixture.WorkClient(outsiderSubject, slug, workspaceId);

        (await outsider.GetAsync(new Uri($"/api/v1/forms/{form.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetAsync(new Uri($"/api/v1/forms/{form.Id}/submissions", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetAsync(new Uri($"/api/v1/forms/{form.Id}/submissions/export.csv", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await outsider.GetAsync(new Uri($"/api/v1/forms/{form.Id}/submissions/export.xlsx", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The actual owner can still read it — proves the gate is membership-based, not "nobody can read".
        var ownerSubmissions = await owner.GetFromJsonAsync<List<FormSubmissionResp>>($"/api/v1/forms/{form.Id}/submissions");
        ownerSubmissions!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Csv_and_excel_export_round_trip_the_submitted_values()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);

        var create = await client.PostAsJsonAsync("/api/v1/forms", new
        {
            listId = list.Id,
            title = "Exportable intake",
            fields = new[] { new { label = "Favorite color", type = "Text", required = true, options = Array.Empty<string>(), position = 0 } },
        });
        var form = (await create.Content.ReadFromJsonAsync<FormResp>())!;
        var fieldId = form.Fields.Single().Id.ToString();

        var anon = fixture.Factory.CreateClient();
        var marker = $"chartreuse-{Guid.NewGuid():N}";
        var submit = await anon.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}/submissions",
            new { values = new Dictionary<string, string> { [fieldId] = marker } });
        submit.EnsureSuccessStatusCode();

        var csvResponse = await client.GetAsync(new Uri($"/api/v1/forms/{form.Id}/submissions/export.csv", UriKind.Relative));
        csvResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var csv = await csvResponse.Content.ReadAsStringAsync();
        csv.ShouldContain("Favorite color");
        csv.ShouldContain(marker);

        var xlsxResponse = await client.GetAsync(new Uri($"/api/v1/forms/{form.Id}/submissions/export.xlsx", UriKind.Relative));
        xlsxResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        xlsxResponse.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var xlsxBytes = await xlsxResponse.Content.ReadAsByteArrayAsync();

        // A real round-trip check (not just "some bytes came back"): unzip the OOXML package (stdlib,
        // no Excel library needed — see FormsXlsxWriter) and confirm the worksheet XML actually contains
        // the submitted value and the header.
        using var zip = new ZipArchive(new MemoryStream(xlsxBytes), ZipArchiveMode.Read);
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
        sheetEntry.ShouldNotBeNull();
        using var reader = new StreamReader(sheetEntry!.Open(), Encoding.UTF8);
        var sheetXml = await reader.ReadToEndAsync();
        sheetXml.ShouldContain("Favorite color");
        sheetXml.ShouldContain(marker);
    }
}
