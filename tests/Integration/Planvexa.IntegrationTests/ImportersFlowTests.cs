namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shouldly;
using Xunit;

// Response shapes for the import endpoints.
internal sealed record ImportJobResp(
    Guid Id, string SourceType, string FileName, string Status, List<string> DetectedColumns, string? ColumnMappingJson,
    string? TargetSpaceName, string? TargetListName, int TotalRows, int CommittedRows, int ErrorCount, DateTimeOffset CreatedAtUtc);
internal sealed record ImportJobRowResp(Guid Id, int RowIndex, string Status, string? ErrorMessage, Guid? CreatedTaskId);

/// <summary>
/// CSV import round trip (upload -> auto-mapped -> validate -> commit) creating real Spaces/Lists/Tasks
/// through the normal authorized WorkManagement services, plus resumability: committing an already-fully
/// -committed job again must not duplicate any task (AGENTS.md rule 13).
/// </summary>
[Collection("api")]
public sealed class ImportersFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Csv_import_creates_a_space_list_and_tasks_with_correctly_mapped_fields()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        const string csv = "Title,Description,Priority\nTask One,First imported task,High\nTask Two,Second imported task,\n";
        var upload = await client.PostAsync(
            new Uri("/api/v1/imports?sourceType=Csv&targetSpaceName=Imported+Space&targetListName=Imported+List", UriKind.Relative),
            CsvContent(csv, "tasks.csv"));

        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var job = (await upload.Content.ReadFromJsonAsync<ImportJobResp>())!;
        job.SourceType.ShouldBe("Csv");
        job.TotalRows.ShouldBe(2);
        // Headers ("Title"/"Description"/"Priority") are recognizable synonyms, so the mapping is guessed
        // automatically — no manual /mapping call needed for this sheet.
        job.ColumnMappingJson.ShouldNotBeNullOrWhiteSpace();

        var validate = await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/validate", UriKind.Relative), null);
        validate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validated = (await validate.Content.ReadFromJsonAsync<ImportJobResp>())!;
        validated.Status.ShouldBe("Validated");
        validated.ErrorCount.ShouldBe(0);

        var commit = await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/commit", UriKind.Relative), null);
        commit.StatusCode.ShouldBe(HttpStatusCode.OK);
        var committed = (await commit.Content.ReadFromJsonAsync<ImportJobResp>())!;
        committed.Status.ShouldBe("Completed");
        committed.CommittedRows.ShouldBe(2);

        // A real Space + List were created (find-or-create by name), and both tasks landed in it with the
        // mapped Priority applied.
        var spaces = await client.GetFromJsonAsync<List<SpaceResp>>("/api/v1/spaces");
        var space = spaces!.Single(s => s.Name == "Imported Space");
        var lists = await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{space.Id}/lists");
        var list = lists!.Single(l => l.Name == "Imported List");
        var tasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks");

        tasks!.Count.ShouldBe(2);
        var taskOne = tasks.Single(t => t.Title == "Task One");
        taskOne.Priority.ShouldBe("High");
        tasks.ShouldContain(t => t.Title == "Task Two");

        var rows = await client.GetFromJsonAsync<List<ImportJobRowResp>>($"/api/v1/imports/{job.Id}/rows");
        rows!.ShouldAllBe(r => r.Status == "Committed" && r.CreatedTaskId != null);
    }

    [Fact]
    public async Task Re_committing_an_already_committed_job_does_not_duplicate_tasks()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        const string csv = "Title\nOnly task\n";
        var upload = await client.PostAsync(
            new Uri("/api/v1/imports?sourceType=Csv&targetSpaceName=Resume+Space&targetListName=Resume+List", UriKind.Relative),
            CsvContent(csv, "one.csv"));
        var job = (await upload.Content.ReadFromJsonAsync<ImportJobResp>())!;

        await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/validate", UriKind.Relative), null);
        var firstCommit = await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/commit", UriKind.Relative), null);
        var firstResult = (await firstCommit.Content.ReadFromJsonAsync<ImportJobResp>())!;
        firstResult.CommittedRows.ShouldBe(1);

        var rowsAfterFirst = await client.GetFromJsonAsync<List<ImportJobRowResp>>($"/api/v1/imports/{job.Id}/rows");
        var taskIdAfterFirst = rowsAfterFirst!.Single().CreatedTaskId;
        taskIdAfterFirst.ShouldNotBeNull();

        // Re-invoking commit is exactly what happens when an interrupted commit is resumed (or the
        // endpoint is retried) — it must be a safe no-op for rows already Committed, per AGENTS.md rule 13.
        var secondCommit = await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/commit", UriKind.Relative), null);
        var secondResult = (await secondCommit.Content.ReadFromJsonAsync<ImportJobResp>())!;
        secondResult.CommittedRows.ShouldBe(1);

        var rowsAfterSecond = await client.GetFromJsonAsync<List<ImportJobRowResp>>($"/api/v1/imports/{job.Id}/rows");
        rowsAfterSecond!.Single().CreatedTaskId.ShouldBe(taskIdAfterFirst);

        var spaces = await client.GetFromJsonAsync<List<SpaceResp>>("/api/v1/spaces");
        var space = spaces!.Single(s => s.Name == "Resume Space");
        var lists = await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{space.Id}/lists");
        var list = lists!.Single(l => l.Name == "Resume List");
        var tasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks");

        // Exactly one task exists — a naive re-commit would have created a second "Only task".
        tasks!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_row_missing_a_required_title_is_reported_invalid_and_not_committed()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        const string csv = "Title,Description\nGood row,ok\n,missing title\n";
        var upload = await client.PostAsync(
            new Uri("/api/v1/imports?sourceType=Csv&targetSpaceName=Errors+Space&targetListName=Errors+List", UriKind.Relative),
            CsvContent(csv, "bad.csv"));
        var job = (await upload.Content.ReadFromJsonAsync<ImportJobResp>())!;

        var validate = await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/validate", UriKind.Relative), null);
        var validated = (await validate.Content.ReadFromJsonAsync<ImportJobResp>())!;
        validated.ErrorCount.ShouldBe(1);

        var rows = await client.GetFromJsonAsync<List<ImportJobRowResp>>($"/api/v1/imports/{job.Id}/rows");
        rows!.Single(r => r.RowIndex == 1).Status.ShouldBe("Invalid");
        rows!.Single(r => r.RowIndex == 0).Status.ShouldBe("Valid");

        var commit = await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/commit", UriKind.Relative), null);
        var committed = (await commit.Content.ReadFromJsonAsync<ImportJobResp>())!;
        committed.CommittedRows.ShouldBe(1);
        committed.Status.ShouldBe("Failed"); // one row still failing
    }

    [Fact]
    public async Task Uploading_an_unimplemented_source_type_returns_a_clean_400()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var upload = await client.PostAsync(
            new Uri("/api/v1/imports?sourceType=ClickUp", UriKind.Relative),
            CsvContent("Title\nSome task\n", "board.csv"));

        upload.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await upload.Content.ReadFromJsonAsync<ProblemDetailsResp>();
        problem!.Detail.ShouldBe("ClickUp import is not yet implemented — needs ClickUp's task export format.");
    }

    [Fact]
    public async Task Csv_row_with_a_matching_assignee_email_assigns_the_task_to_that_member()
    {
        var (client, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (memberUserId, memberEmail) = await InviteMemberWithEmailAsync(client, workspaceId, "importee");

        var csv = $"Title,Assignee\nDo the thing,{memberEmail}\n";
        var upload = await client.PostAsync(
            new Uri("/api/v1/imports?sourceType=Csv&targetSpaceName=Assignee+Space&targetListName=Assignee+List", UriKind.Relative),
            CsvContent(csv, "assignees.csv"));
        var job = (await upload.Content.ReadFromJsonAsync<ImportJobResp>())!;
        job.ColumnMappingJson.ShouldNotBeNullOrWhiteSpace(); // "Assignee" is a recognized synonym, auto-mapped.

        await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/validate", UriKind.Relative), null);
        var commit = await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/commit", UriKind.Relative), null);
        var committed = (await commit.Content.ReadFromJsonAsync<ImportJobResp>())!;
        committed.Status.ShouldBe("Completed");

        var spaces = await client.GetFromJsonAsync<List<SpaceResp>>("/api/v1/spaces");
        var space = spaces!.Single(s => s.Name == "Assignee Space");
        var lists = await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{space.Id}/lists");
        var list = lists!.Single(l => l.Name == "Assignee List");
        var tasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks");

        tasks!.Single(t => t.Title == "Do the thing").AssigneeUserIds.ShouldContain(memberUserId);
    }

    [Fact]
    public async Task Csv_row_with_an_unrecognized_assignee_still_creates_the_task_unassigned()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        const string csv = "Title,Assignee\nOrphan task,nobody@nowhere.test\n";
        var upload = await client.PostAsync(
            new Uri("/api/v1/imports?sourceType=Csv&targetSpaceName=Unmatched+Space&targetListName=Unmatched+List", UriKind.Relative),
            CsvContent(csv, "unmatched.csv"));
        var job = (await upload.Content.ReadFromJsonAsync<ImportJobResp>())!;

        var validate = await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/validate", UriKind.Relative), null);
        var validated = (await validate.Content.ReadFromJsonAsync<ImportJobResp>())!;
        validated.ErrorCount.ShouldBe(0); // an unresolvable assignee is not a validation failure

        var commit = await client.PostAsync(new Uri($"/api/v1/imports/{job.Id}/commit", UriKind.Relative), null);
        var committed = (await commit.Content.ReadFromJsonAsync<ImportJobResp>())!;
        committed.Status.ShouldBe("Completed");
        committed.CommittedRows.ShouldBe(1);

        var spaces = await client.GetFromJsonAsync<List<SpaceResp>>("/api/v1/spaces");
        var space = spaces!.Single(s => s.Name == "Unmatched Space");
        var lists = await client.GetFromJsonAsync<List<ListResp>>($"/api/v1/spaces/{space.Id}/lists");
        var list = lists!.Single(l => l.Name == "Unmatched List");
        var tasks = await client.GetFromJsonAsync<List<TaskResp>>($"/api/v1/lists/{list.Id}/tasks");

        tasks!.Single(t => t.Title == "Orphan task").AssigneeUserIds.ShouldBeEmpty();
    }

    private sealed record ProblemDetailsResp(string? Title, string? Detail);

    /// <summary>Like <c>WorkTestHelpers.InviteMemberAsync</c>, but also returns the accepting member's
    /// real email — needed here to build a CSV row whose Assignee column matches it. Kept local rather
    /// than changing the shared helper's tuple shape, which dozens of other tests destructure as
    /// (subject, userId).</summary>
    private async Task<(Guid UserId, string Email)> InviteMemberWithEmailAsync(HttpClient ownerClient, Guid workspaceId, string emailPrefix)
    {
        var subject = TestData.NewSubject();
        var invitedEmail = $"{subject}@planvexa.test";
        var inviteResponse = await ownerClient.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/invitations", new { email = invitedEmail, role = "Member" });
        inviteResponse.EnsureSuccessStatusCode();

        var token = fixture.LastInvitationToken(invitedEmail) ?? throw new InvalidOperationException($"No invitation email was recorded for {invitedEmail}.");
        var accept = await fixture.AuthClient(subject).PostAsync(new Uri($"/api/v1/invitations/{token}/accept", UriKind.Relative), null);
        accept.EnsureSuccessStatusCode();
        var accepted = await accept.Content.ReadFromJsonAsync<AcceptResponse>();

        var members = await ownerClient.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members");
        var userId = members!.Single(m => m.Id == accepted!.MembershipId).UserId;
        return (userId, invitedEmail);
    }

    private static MultipartFormDataContent CsvContent(string csv, string fileName)
    {
        var part = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { part, "file", fileName } };
    }
}
