namespace Planvexa.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Planvexa.Api.Auth;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.TimeTracking.Application.Services;
using Shouldly;
using Xunit;

internal sealed record TimeTagResp(Guid Id, string Name);
internal sealed record TimeEntryTagsResp(Guid Id, List<TimeTagResp> Tags);
internal sealed record BudgetResp(Guid Id, string Name, string ScopeType, Guid ScopeId, decimal? MonetaryCapAmount, long? TimeCapSeconds);
internal sealed record BudgetStatusResp(
    Guid BudgetId, string Name, string ScopeType, Guid ScopeId,
    decimal? MonetaryCapAmount, long? TimeCapSeconds,
    decimal Hours, decimal Cost, decimal Revenue, decimal Profit,
    decimal? MonetaryConsumedPercent, decimal? TimeConsumedPercent);

/// <summary>
/// Time tracking polish: tags on entries, project budgets/profitability, the accounting
/// CSV export, and the missing-time reminder scheduler -- plus confirming all the new cost/rate-bearing
/// endpoints stay Admin+ gated, the same bar the report/rate endpoints already sit behind.
/// </summary>
[Collection("api")]
public sealed class TimeTrackingPolishTests(PlanvexaFixture fixture)
{
    [Fact]
    public async Task Tags_can_be_created_attached_to_an_entry_and_used_to_filter_the_report()
    {
        var (owner, _, _, _) = await fixture.NewWorkspaceClientAsync();

        var tagResponse = await owner.PostAsJsonAsync("/api/v1/time-tags", new { name = "Client Work" });
        tagResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tag = await tagResponse.Content.ReadFromJsonAsync<TimeTagResp>();

        // Creating the same name again is idempotent (returns the existing tag).
        var again = await owner.PostAsJsonAsync("/api/v1/time-tags", new { name = "Client Work" });
        (await again.Content.ReadFromJsonAsync<TimeTagResp>())!.Id.ShouldBe(tag!.Id);

        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");
        var create = await owner.PostAsJsonAsync("/api/v1/time-entries", new
        {
            startedAtUtc = start,
            endedAtUtc = start.AddHours(1),
            description = "tagged work",
            timeZoneId = "UTC",
            tagIds = new[] { tag.Id },
        });
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var entry = await create.Content.ReadFromJsonAsync<TimeEntryTagsResp>();
        entry!.Tags.ShouldContain(t => t.Id == tag.Id && t.Name == "Client Work");

        // An untagged entry in the same range should not show up in a tag-filtered report.
        await owner.PostAsJsonAsync("/api/v1/time-entries", new
        {
            startedAtUtc = start,
            endedAtUtc = start.AddMinutes(30),
            description = "untagged work",
            timeZoneId = "UTC",
        });

        var from = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-03-08T00:00:00Z");
        var filtered = await owner.GetFromJsonAsync<List<ReportRowResp>>(
            $"/api/v1/reports/time?groupBy=user&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&tagId={tag.Id}");

        filtered!.Single().Hours.ShouldBe(1m); // only the 1h tagged entry, not the untagged 30m one
    }

    [Fact]
    public async Task Budget_status_reports_consumption_and_profitability_from_real_entries()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var me = (await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members"))!.Single();
        await owner.PutAsJsonAsync($"/api/v1/rates/user/{me.UserId}", new { billingRate = 100m, costRate = 40m });

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Budgeted work");

        var budgetResponse = await owner.PostAsJsonAsync("/api/v1/budgets", new
        {
            scopeType = "List",
            scopeId = list.Id,
            name = "Sprint 1 budget",
            monetaryCapAmount = 200m,
            timeCapSeconds = (long?)null,
        });
        budgetResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var budget = await budgetResponse.Content.ReadFromJsonAsync<BudgetResp>();

        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");
        await owner.PostAsJsonAsync("/api/v1/time-entries", new
        {
            taskId = task.Id,
            startedAtUtc = start,
            endedAtUtc = start.AddHours(2), // 2h * 40 cost = 80, 2h * 100 billing = 200
            isBillable = true,
            timeZoneId = "UTC",
        });

        var from = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-03-08T00:00:00Z");
        var status = await owner.GetFromJsonAsync<BudgetStatusResp>(
            $"/api/v1/budgets/{budget!.Id}/status?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}");

        status!.Hours.ShouldBe(2m);
        status.Cost.ShouldBe(80m);
        status.Revenue.ShouldBe(200m);
        status.Profit.ShouldBe(120m);
        // Consumption is measured against cost (80), not revenue: 80 / 200 cap = 40%.
        status.MonetaryConsumedPercent.ShouldBe(40m);
        status.TimeConsumedPercent.ShouldBeNull(); // no time cap set
    }

    [Fact]
    public async Task Accounting_export_uses_the_qbo_time_activity_column_layout()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var me = (await owner.GetFromJsonAsync<List<MemberResponse>>($"/api/v1/workspaces/{workspaceId}/members"))!.Single();
        await owner.PutAsJsonAsync($"/api/v1/rates/user/{me.UserId}", new { billingRate = 50m, costRate = 20m });

        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);
        var task = await owner.CreateTaskAsync(list.Id, "Exported task");

        var start = DateTimeOffset.Parse("2026-03-02T09:00:00Z");
        await owner.PostAsJsonAsync("/api/v1/time-entries", new
        {
            taskId = task.Id,
            startedAtUtc = start,
            endedAtUtc = start.AddHours(1),
            description = "billable hour",
            isBillable = true,
            timeZoneId = "UTC",
        });

        var from = DateTimeOffset.Parse("2026-03-01T00:00:00Z");
        var to = DateTimeOffset.Parse("2026-03-08T00:00:00Z");
        var response = await owner.GetAsync(new Uri(
            $"/api/v1/reports/time/export/accounting?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}",
            UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lines[0].ShouldBe("TxnDate,Employee,CustomerJob,ServiceItem,DurationHours,BillableStatus,Notes,HourlyRate,Amount");
        lines[1].ShouldContain("2026-03-02");
        lines[1].ShouldContain("Exported task");
        lines[1].ShouldContain("Billable");
        lines[1].ShouldContain("50");
    }

    [Fact]
    public async Task Missing_time_reminder_notifies_only_members_short_of_the_minimum()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var (shortSubject, shortUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "shorttime");
        var (fullSubject, fullUserId) = await fixture.InviteMemberAsync(owner, workspaceId, "fulltime");
        _ = shortSubject;
        _ = fullSubject;

        // Enable a daily reminder requiring at least 4 hours logged.
        var policyResponse = await owner.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/time-policy");
        (await owner.PutAsJsonAsync("/api/v1/time-policy", new
        {
            singleActiveTimer = policyResponse.GetProperty("singleActiveTimer").GetBoolean(),
            roundingMinutes = 0,
            minimumDurationSeconds = 0,
            maximumEntrySeconds = 86400,
            billableByDefault = true,
            requireDescription = false,
            requireTask = false,
            editWindowHours = 0,
            approvalRequired = false,
            weekStartsOn = 1,
            lockDateUtc = (DateTimeOffset?)null,
            overtimeThresholdSeconds = 144000,
            missingTimeReminderEnabled = true,
            missingTimeReminderCadence = "Daily",
            missingTimeReminderMinimumSeconds = 4 * 3600,
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The "full time" member logs 5h today; the "short time" member logs nothing.
        var fullClient = fixture.WorkClient(fullSubject, workspaceId);
        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(now.Date, TimeSpan.Zero);
        await fullClient.PostAsJsonAsync("/api/v1/time-entries", new
        {
            startedAtUtc = todayStart.AddHours(1),
            endedAtUtc = todayStart.AddHours(6),
            description = "full day",
            timeZoneId = "UTC",
        });

        await RunMissingTimeReminderOnceAsync(workspaceId);

        var shortClient = fixture.WorkClient(shortSubject, workspaceId);
        var shortNotifications = await shortClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications");
        shortNotifications!.ShouldContain(n => n.EventType == "time.missing_time_reminder");

        var fullNotifications = await fullClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications");
        fullNotifications!.ShouldNotContain(n => n.EventType == "time.missing_time_reminder");

        // Running it again the same day is idempotent -- still exactly one reminder for the short member.
        await RunMissingTimeReminderOnceAsync(workspaceId);
        var shortNotificationsAgain = await shortClient.GetFromJsonAsync<List<NotificationResp>>("/api/v1/notifications");
        shortNotificationsAgain!.Count(n => n.EventType == "time.missing_time_reminder").ShouldBe(1);
    }

    [Fact]
    public async Task Member_without_report_access_cannot_read_budget_status_or_the_accounting_export()
    {
        var (owner, workspaceId, _, _) = await fixture.NewWorkspaceClientAsync();
        var space = await owner.CreateSpaceAsync();
        var list = await owner.CreateListAsync(space.Id);

        var budgetResponse = await owner.PostAsJsonAsync("/api/v1/budgets", new
        {
            scopeType = "List",
            scopeId = list.Id,
            name = "Members must not see this",
            monetaryCapAmount = 500m,
            timeCapSeconds = (long?)null,
        });
        var budget = await budgetResponse.Content.ReadFromJsonAsync<BudgetResp>();

        var (memberSubject, _) = await fixture.InviteMemberAsync(owner, workspaceId, "noreports");
        var member = fixture.WorkClient(memberSubject, workspaceId);

        var from = Uri.EscapeDataString(DateTimeOffset.Parse("2026-03-01T00:00:00Z").ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.Parse("2026-03-08T00:00:00Z").ToString("O"));

        // A Member can track their own time but cannot see workspace cost/rate rollups: budget status,
        // the accounting export, and creating a budget are all Admin+ (TimeAuthorizer.EnsureManage),
        // same gate as the existing rates/utilization endpoints.
        (await member.GetAsync(new Uri($"/api/v1/budgets/{budget!.Id}/status?from={from}&to={to}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await member.GetAsync(new Uri($"/api/v1/reports/time/export/accounting?from={from}&to={to}", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await member.PostAsJsonAsync("/api/v1/budgets", new { scopeType = "List", scopeId = list.Id, name = "nope", monetaryCapAmount = 1m, timeCapSeconds = (long?)null }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await member.GetAsync(new Uri("/api/v1/budgets", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Invokes <see cref="MissingTimeReminderRunner.RunAsync"/> directly under a bound workspace
    /// context, mirroring CollaborationPolishFlowTests' RunDigestOnceAsync — the real cadence (daily/weekly, plus the
    /// 23:00 UTC "period is over" cutoff) is not something a test should wait for.
    /// </summary>
    private async Task RunMissingTimeReminderOnceAsync(Guid workspaceId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IWorkspaceContextAccessor>().Set(new WorkspaceContext(
            workspaceId, PlatformSystemUserId, null, string.Empty, new HashSet<string>(), new HashSet<string>(), "test-missing-time"));
        scope.ServiceProvider.GetRequiredService<CurrentUser>().Set(PlatformSystemUserId, "system", "system@planvexa.test", "System");

        var policyStore = scope.ServiceProvider.GetRequiredService<Planvexa.Modules.TimeTracking.Application.ITimePolicyStore>();
        var policy = await policyStore.FindAsync(workspaceId)
            ?? throw new InvalidOperationException("Time policy not found for the given workspace.");

        var runner = scope.ServiceProvider.GetRequiredService<MissingTimeReminderRunner>();

        // Force "the period is over" so the test does not depend on wall-clock time — evaluate the
        // reminder as of the last moment of today's UTC day, exactly like the real 23:00 cutoff would.
        var endOfToday = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero).AddHours(23);
        await runner.RunAsync(policy, endOfToday);
    }

    private static readonly Guid PlatformSystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
}
