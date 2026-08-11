namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

internal sealed record TimeEntryResp(
    Guid Id, Guid UserId, Guid? TaskId, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc,
    long DurationSeconds, string TimeZoneId, string? Description, bool IsBillable,
    decimal BillingRate, decimal CostRate, string Source, string ApprovalStatus,
    bool IsPaused = false, DateTimeOffset? PausedAtUtc = null, long PausedSeconds = 0);

internal sealed record TimesheetResp(
    Guid Id, Guid UserId, DateTimeOffset PeriodStartUtc, DateTimeOffset PeriodEndUtc, string Status,
    long TotalSeconds, long BillableSeconds, decimal Revenue, decimal Cost, List<TimeEntryResp> Entries);

internal sealed record ReportRowResp(string Key, string Label, decimal Hours, decimal BillableHours, decimal Cost, decimal Revenue);

[Collection("api")]
public sealed class TimeTrackingFlowTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Timer_start_survives_and_stop_computes_duration()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await client.CreateSpaceAsync();
        var list = await client.CreateListAsync(space.Id);
        var task = await client.CreateTaskAsync(list.Id, "Timed work");

        var startResponse = await client.PostAsJsonAsync("/api/v1/timers/start", new { taskId = task.Id, description = "coding" });
        startResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var started = await startResponse.Content.ReadFromJsonAsync<TimeEntryResp>();
        started!.EndedAtUtc.ShouldBeNull();

        // "Survives": a fresh request still finds the active timer (persisted server-side).
        var active = await client.GetFromJsonAsync<TimeEntryResp>("/api/v1/timers/active");
        active!.Id.ShouldBe(started.Id);

        // Stop computes a non-negative duration from server timestamps.
        var stopResponse = await client.PostAsJsonAsync("/api/v1/timers/stop", new { description = "done" });
        stopResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stopped = await stopResponse.Content.ReadFromJsonAsync<TimeEntryResp>();
        stopped!.EndedAtUtc.ShouldNotBeNull();
        stopped.DurationSeconds.ShouldBeGreaterThanOrEqualTo(0);

        // No active timer remains.
        var afterStop = await client.GetAsync(new Uri("/api/v1/timers/active", UriKind.Relative));
        var body = await afterStop.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("active", out var activeProp).ShouldBeTrue();
        activeProp.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>
    /// Offline-mutation-outbox replay guard: a timer start replayed with the same Idempotency-Key header
    /// must return the ORIGINAL running entry, not insert a second row (and not trip the single-active-
    /// timer conflict) — see TimeEntryService.StartTimerAsync's idempotency check.
    /// </summary>
    [Fact]
    public async Task Repeated_timer_start_with_the_same_idempotency_key_does_not_duplicate()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var idempotencyKey = Guid.NewGuid().ToString();

        async Task<TimeEntryResp> StartWithKeyAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/timers/start")
            {
                Content = JsonContent.Create(new { description = "offline-started timer" }),
            };
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            var response = await client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<TimeEntryResp>())!;
        }

        var first = await StartWithKeyAsync();
        var replay = await StartWithKeyAsync();

        replay.Id.ShouldBe(first.Id);

        var active = await client.GetFromJsonAsync<TimeEntryResp>("/api/v1/timers/active");
        active!.Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Second_concurrent_timer_start_is_rejected()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var first = await client.PostAsJsonAsync("/api/v1/timers/start", new { description = "first" });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/api/v1/timers/start", new { description = "second" });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Db_partial_unique_index_enforces_single_active_timer()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        await client.PostAsJsonAsync("/api/v1/timers/start", new { description = "running" });

        var userId = await GetFirstUserIdAsync();

        // Attempt to insert a second running entry directly, bypassing the service — the DB rejects it.
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO time.time_entries
              (id, workspace_id, user_id, task_id, started_at_utc, ended_at_utc, duration_seconds,
               time_zone_id, description, is_billable, billing_rate, cost_rate, source, approval_status, created_at_utc)
            SELECT gen_random_uuid(), workspace_id, user_id, NULL, now(), NULL, 0,
               'UTC', 'dupe', false, 0, 0, 'Timer', 'Draft', now()
            FROM time.time_entries WHERE user_id = @u AND ended_at_utc IS NULL LIMIT 1;
            """;
        command.Parameters.AddWithValue("u", userId);

        var ex = await Should.ThrowAsync<Npgsql.PostgresException>(async () => await command.ExecuteNonQueryAsync());
        ex.SqlState.ShouldBe("23505"); // unique_violation
    }

    /// <summary>Server-authoritative pause: the paused interval is excluded from the final duration,
    /// no matter how long it lasts client-side (TimeEntry.Pause/Stop).</summary>
    [Fact]
    public async Task Timer_pause_excludes_paused_time_from_duration()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var start = await client.PostAsJsonAsync("/api/v1/timers/start", new { description = "pause me" });
        start.StatusCode.ShouldBe(HttpStatusCode.Created);

        var pause = await client.PostAsync(new Uri("/api/v1/timers/pause", UriKind.Relative), null);
        pause.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paused = await pause.Content.ReadFromJsonAsync<TimeEntryResp>();
        paused!.IsPaused.ShouldBeTrue();

        // Pausing twice is rejected -- can't pause an already-paused timer.
        var pauseAgain = await client.PostAsync(new Uri("/api/v1/timers/pause", UriKind.Relative), null);
        pauseAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Time passes while paused; this must not count toward the duration.
        await Task.Delay(TimeSpan.FromSeconds(2.5));

        var stopResponse = await client.PostAsJsonAsync("/api/v1/timers/stop", new { });
        stopResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stopped = await stopResponse.Content.ReadFromJsonAsync<TimeEntryResp>();

        // Only the (sub-second) running time before/after the pause counts -- well under the 2.5s
        // that was excluded by the pause.
        stopped!.DurationSeconds.ShouldBeLessThanOrEqualTo(1);
    }

    /// <summary>Resuming a paused timer makes it accrue duration again from server timestamps.</summary>
    [Fact]
    public async Task Timer_resume_continues_accruing_duration()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();

        await client.PostAsJsonAsync("/api/v1/timers/start", new { description = "resume me" });
        await client.PostAsync(new Uri("/api/v1/timers/pause", UriKind.Relative), null);

        // Resuming a timer that isn't paused is rejected.
        var doubleResume = await client.PostAsync(new Uri("/api/v1/timers/resume", UriKind.Relative), null);
        doubleResume.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resumeAgain = await client.PostAsync(new Uri("/api/v1/timers/resume", UriKind.Relative), null);
        resumeAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var active = await client.GetFromJsonAsync<TimeEntryResp>("/api/v1/timers/active");
        active!.IsPaused.ShouldBeFalse();

        await Task.Delay(TimeSpan.FromSeconds(1.2));

        var stopResponse = await client.PostAsJsonAsync("/api/v1/timers/stop", new { });
        var stopped = await stopResponse.Content.ReadFromJsonAsync<TimeEntryResp>();
        stopped!.DurationSeconds.ShouldBeGreaterThanOrEqualTo(1);
    }

    /// <summary>Only the timer's own owner can pause/resume it: a different workspace member has no
    /// active timer of their own, so pause/resume 404s rather than ever touching the owner's timer.</summary>
    [Fact]
    public async Task Member_cannot_pause_or_resume_another_users_timer()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var start = await owner.PostAsJsonAsync("/api/v1/timers/start", new { description = "owner's timer" });
        var ownerEntry = await start.Content.ReadFromJsonAsync<TimeEntryResp>();

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "ttp");
        var member = fixture.WorkClient(memberSubject, workspaceId);

        var pause = await member.PostAsync(new Uri("/api/v1/timers/pause", UriKind.Relative), null);
        pause.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var resume = await member.PostAsync(new Uri("/api/v1/timers/resume", UriKind.Relative), null);
        resume.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The owner's timer is untouched -- still running and not paused.
        var active = await owner.GetFromJsonAsync<TimeEntryResp>("/api/v1/timers/active");
        active!.Id.ShouldBe(ownerEntry!.Id);
        active.IsPaused.ShouldBeFalse();
    }

    [Fact]
    public async Task Manual_entry_edit_is_audited()
    {
        var (client, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");
        var create = await client.PostAsJsonAsync("/api/v1/time-entries", new
        {
            startedAtUtc = start,
            endedAtUtc = start.AddHours(1),
            description = "manual",
            timeZoneId = "UTC",
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var entry = await create.Content.ReadFromJsonAsync<TimeEntryResp>();
        entry!.DurationSeconds.ShouldBe(3600);

        var patch = await client.PatchAsJsonAsync($"/api/v1/time-entries/{entry.Id}", new { endedAtUtc = start.AddHours(2) });
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = await patch.Content.ReadFromJsonAsync<TimeEntryResp>();
        edited!.DurationSeconds.ShouldBe(7200);

        // An audit row exists for the edit.
        var auditCount = await CountAuditsAsync(entry.Id, "entry.edited");
        auditCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Approved_period_is_immutable_and_edit_requires_reason()
    {
        // Owner sets rates then a member logs time; owner approves and locks.
        var (owner, workspaceId, slug, _) = await fixture.NewWorkspaceClientAsync();
        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");

        var create = await owner.PostAsJsonAsync("/api/v1/time-entries", new
        {
            startedAtUtc = start,
            endedAtUtc = start.AddHours(2),
            description = "work",
            isBillable = true,
            timeZoneId = "UTC",
        });
        var entry = await create.Content.ReadFromJsonAsync<TimeEntryResp>();

        // Submit + approve the week.
        var weekStart = DateTimeOffset.Parse("2026-03-02T00:00:00Z");
        await owner.PostAsJsonAsync("/api/v1/timesheets/submit", new { weekStartUtc = weekStart });
        var timesheet = await owner.GetFromJsonAsync<TimesheetResp>($"/api/v1/timesheets?weekStart={Uri.EscapeDataString(weekStart.ToString("O"))}");
        var approve = await owner.PostAsync(new Uri($"/api/v1/timesheets/{timesheet!.Id}/approve", UriKind.Relative), null);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Editing the now-approved entry without a reason is rejected (409).
        var noReason = await owner.PatchAsJsonAsync($"/api/v1/time-entries/{entry!.Id}", new { endedAtUtc = start.AddHours(3) });
        noReason.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // With a reason it succeeds (returns to Draft).
        var withReason = await owner.PatchAsJsonAsync($"/api/v1/time-entries/{entry.Id}", new { endedAtUtc = start.AddHours(3), reason = "client correction" });
        withReason.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Lock the period → entries become immutable even with a reason.
        await owner.PostAsJsonAsync("/api/v1/timesheets/submit", new { weekStartUtc = weekStart });
        var timesheet2 = await owner.GetFromJsonAsync<TimesheetResp>($"/api/v1/timesheets?weekStart={Uri.EscapeDataString(weekStart.ToString("O"))}");
        await owner.PostAsync(new Uri($"/api/v1/timesheets/{timesheet2!.Id}/approve", UriKind.Relative), null);
        await owner.PostAsync(new Uri($"/api/v1/timesheets/{timesheet2.Id}/lock", UriKind.Relative), null);

        var afterLock = await owner.PatchAsJsonAsync($"/api/v1/time-entries/{entry.Id}", new { endedAtUtc = start.AddHours(4), reason = "too late" });
        afterLock.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        _ = (workspaceId, slug);
    }

    [Fact]
    public async Task Admin_reopens_a_locked_timesheet_and_entries_become_editable_again()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");

        var create = await owner.PostAsJsonAsync("/api/v1/time-entries", new
        {
            startedAtUtc = start,
            endedAtUtc = start.AddHours(2),
            description = "work",
            timeZoneId = "UTC",
        });
        var entry = await create.Content.ReadFromJsonAsync<TimeEntryResp>();

        // Submit, approve, and lock the week.
        var weekStart = DateTimeOffset.Parse("2026-03-02T00:00:00Z");
        await owner.PostAsJsonAsync("/api/v1/timesheets/submit", new { weekStartUtc = weekStart });
        var timesheet = await owner.GetFromJsonAsync<TimesheetResp>($"/api/v1/timesheets?weekStart={Uri.EscapeDataString(weekStart.ToString("O"))}");
        await owner.PostAsync(new Uri($"/api/v1/timesheets/{timesheet!.Id}/approve", UriKind.Relative), null);
        await owner.PostAsync(new Uri($"/api/v1/timesheets/{timesheet.Id}/lock", UriKind.Relative), null);

        // A non-Admin (Member) may not reopen it.
        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "tro");
        var member = fixture.WorkClient(memberSubject, workspaceId);
        var memberReopen = await member.PostAsync(new Uri($"/api/v1/timesheets/{timesheet.Id}/reopen", UriKind.Relative), null);
        memberReopen.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A caller with no access to this workspace at all can't even see the period (workspace-scoped
        // row-level security hides it entirely, so the lookup 404s rather than 403s).
        var (outsider, _, _, _) = await fixture.NewWorkspaceClientAsync();
        var outsiderReopen = await outsider.PostAsync(new Uri($"/api/v1/timesheets/{timesheet.Id}/reopen", UriKind.Relative), null);
        outsiderReopen.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The period is still locked and the entry still immutable.
        var stillLocked = await owner.PatchAsJsonAsync($"/api/v1/time-entries/{entry!.Id}", new { endedAtUtc = start.AddHours(3), reason = "should still fail" });
        stillLocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The Admin (owner) reopens it: the period returns to Draft and the entry becomes editable again.
        var reopen = await owner.PostAsync(new Uri($"/api/v1/timesheets/{timesheet.Id}/reopen", UriKind.Relative), null);
        reopen.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reopened = await reopen.Content.ReadFromJsonAsync<TimesheetResp>();
        reopened!.Status.ShouldBe("Draft");

        var editAfterReopen = await owner.PatchAsJsonAsync($"/api/v1/time-entries/{entry.Id}", new { endedAtUtc = start.AddHours(3) });
        editAfterReopen.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = await editAfterReopen.Content.ReadFromJsonAsync<TimeEntryResp>();
        edited!.ApprovalStatus.ShouldBe("Draft");
    }

    [Fact]
    public async Task Report_totals_reconcile_with_entries_using_decimal_money()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();

        // Set the owner's rate: billing 100.50, cost 40.
        var me = (await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members"))!.Single();
        await owner.PutAsJsonAsync($"/api/v1/rates/user/{me.UserId}", new { billingRate = 100.50m, costRate = 40m });

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Billable");

        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");
        await owner.PostAsJsonAsync("/api/v1/time-entries", new
        {
            taskId = task.Id,
            startedAtUtc = start,
            endedAtUtc = start.AddMinutes(90), // 1.5h
            isBillable = true,
            timeZoneId = "UTC",
        });

        var from = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-03-08T00:00:00Z");
        var rows = await owner.GetFromJsonAsync<List<ReportRowResp>>(
            $"/api/v1/reports/time?groupBy=user&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");

        var row = rows!.Single(r => r.Key == me.UserId.ToString());
        row.Hours.ShouldBe(1.5m);
        // 1.5h * 100.50 = 150.75 exactly (decimal).
        row.Revenue.ShouldBe(150.75m);
        row.Cost.ShouldBe(60m);
    }

    [Fact]
    public async Task Report_labels_are_names_not_identifiers()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var me = (await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members"))!.Single();

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Write the launch checklist");

        var start = DateTimeOffset.Parse("2026-04-06T09:00:00Z");
        await owner.PostAsJsonAsync("/api/v1/time-entries", new
        {
            taskId = task.Id,
            startedAtUtc = start,
            endedAtUtc = start.AddMinutes(30),
            isBillable = true,
            timeZoneId = "UTC",
        });

        var from = Uri.EscapeDataString(DateTimeOffset.Parse("2026-04-01T00:00:00Z").ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.Parse("2026-04-13T00:00:00Z").ToString("O"));

        async Task<ReportRowResp> SingleRowAsync(string groupBy)
        {
            var rows = await owner.GetFromJsonAsync<List<ReportRowResp>>(
                $"/api/v1/reports/time?groupBy={groupBy}&from={from}&to={to}");
            return rows!.Single();
        }

        // The key stays machine-readable; the label must be something a human recognises.
        var byUser = await SingleRowAsync("user");
        byUser.Key.ShouldBe(me.UserId.ToString());
        byUser.Label.ShouldNotBe(me.UserId.ToString());
        Guid.TryParse(byUser.Label, out _).ShouldBeFalse();

        var byTask = await SingleRowAsync("task");
        byTask.Label.ShouldBe("Write the launch checklist");

        // "Project" groups by the containing list, so the label is the list's name — not a task id
        // and not the literal word "Project", which is what every row used to say.
        var byProject = await SingleRowAsync("project");
        byProject.Key.ShouldBe(list.Id.ToString());
        byProject.Label.ShouldBe(list.Name);
    }

    private async Task<Guid> GetFirstUserIdAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id FROM time.time_entries WHERE ended_at_utc IS NULL LIMIT 1";
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task<int> CountAuditsAsync(Guid entryId, string action)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM time.time_entry_audits WHERE time_entry_id = @e AND action = @a";
        command.Parameters.AddWithValue("e", entryId);
        command.Parameters.AddWithValue("a", action);
        return (int)(long)(await command.ExecuteScalarAsync())!;
    }
}
