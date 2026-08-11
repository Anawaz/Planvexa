namespace Planvexa.Modules.TimeTracking.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.TimeTracking.Authorization;
using Planvexa.Modules.TimeTracking.Domain;

public sealed class TimePolicyService(TimeServiceContext ctx, IMemberRateStore rates) : TimeServiceBase(ctx)
{
    public async Task<TimePolicyDto> GetAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);
        var policy = await GetOrCreatePolicyAsync(workspaceId, ct);
        await SaveAsync(ct);
        return TimeMapper.ToDto(policy);
    }

    public async Task<TimePolicyDto> UpdateAsync(UpdatePolicyCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var policy = await GetOrCreatePolicyAsync(workspaceId, ct);
        policy.Update(
            command.SingleActiveTimer, command.RoundingMinutes, command.MinimumDurationSeconds, command.MaximumEntrySeconds,
            command.BillableByDefault, command.RequireDescription, command.RequireTask, command.EditWindowHours,
            command.ApprovalRequired, command.WeekStartsOn, command.LockDateUtc, command.OvertimeThresholdSeconds,
            command.MissingTimeReminderEnabled, command.MissingTimeReminderCadence, command.MissingTimeReminderMinimumSeconds);

        Audit("time.policy_updated", "TimePolicy", policy.Id);
        await SaveAsync(ct);
        return TimeMapper.ToDto(policy);
    }

    public async Task<IReadOnlyList<MemberRateDto>> ListRatesAsync(CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);
        var list = await rates.ListByWorkspaceAsync(workspaceId, ct);
        return list.Where(r => r.ProjectId is null).Select(r => new MemberRateDto(r.UserId, r.BillingRate, r.CostRate)).ToList();
    }

    public async Task<MemberRateDto> SetUserRateAsync(Guid userId, SetRateCommand command, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var existing = await rates.FindAsync(workspaceId, userId, null, ct);
        if (existing is null)
        {
            existing = MemberRate.Create(NewId(), workspaceId, userId, null, command.BillingRate, command.CostRate);
            rates.Add(existing);
        }
        else
        {
            existing.Update(command.BillingRate, command.CostRate);
        }

        Audit("time.rate_set", "MemberRate", existing.Id, new { userId });
        await SaveAsync(ct);
        return new MemberRateDto(userId, existing.BillingRate, existing.CostRate);
    }
}

public sealed class TimesheetService(
    TimeServiceContext ctx,
    ITimeEntryStore entries,
    ITimesheetStore timesheets) : TimeServiceBase(ctx)
{
    public async Task<TimesheetDto> GetWeekAsync(DateTimeOffset weekStartUtc, Guid? userId, Guid? tagId, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        var access = await AccessAsync(workspaceId, ct);
        TimeAuthorizer.EnsureTrackOwn(access?.Role);

        var targetUser = TimeAuthorizer.CanManage(access?.Role) ? (userId ?? UserId) : UserId;

        var policy = await GetOrCreatePolicyAsync(workspaceId, ct);
        var start = TimeMath.StartOfLocalWeek(weekStartUtc, TimeMath.ResolveTimeZone("UTC"), policy.WeekStartsOn);
        var end = start.AddDays(7);

        var period = await timesheets.FindForUserWeekAsync(workspaceId, targetUser, start, ct);
        var periodEntries = await entries.ListForPeriodAsync(workspaceId, targetUser, start, end, ct);
        if (tagId is { } tag)
        {
            periodEntries = periodEntries.Where(e => e.Tags.Any(t => t.TagId == tag)).ToList();
        }

        await SaveAsync(ct);

        return BuildDto(period, targetUser, start, end, periodEntries);
    }

    public async Task<TimesheetDto> SubmitAsync(DateTimeOffset weekStartUtc, CancellationToken ct = default)
    {
        var workspaceId = RequireWorkspace();
        TimeAuthorizer.EnsureTrackOwn((await AccessAsync(workspaceId, ct))?.Role);

        var policy = await GetOrCreatePolicyAsync(workspaceId, ct);
        var start = TimeMath.StartOfLocalWeek(weekStartUtc, TimeMath.ResolveTimeZone("UTC"), policy.WeekStartsOn);
        var end = start.AddDays(7);

        var period = await timesheets.FindForUserWeekAsync(workspaceId, UserId, start, ct);
        if (period is null)
        {
            period = TimesheetPeriod.Create(NewId(), workspaceId, UserId, start, end, TimesheetCadence.Weekly);
            timesheets.Add(period);
        }

        period.Submit(Now);

        // Submit the period's entries too.
        var periodEntries = await entries.ListForPeriodAsync(workspaceId, UserId, start, end, ct);
        foreach (var entry in periodEntries.Where(e => !e.IsRunning))
        {
            entry.Submit();
        }

        Audit("time.timesheet_submitted", "TimesheetPeriod", period.Id, new { start });
        await SaveAsync(ct);
        return BuildDto(period, UserId, start, end, periodEntries);
    }

    public async Task<TimesheetDto> ApproveAsync(Guid periodId, string? comment, bool approve, CancellationToken ct = default)
    {
        var period = await timesheets.FindAsync(periodId, ct) ?? throw new NotFoundException("Timesheet not found.");
        TimeAuthorizer.EnsureManage((await AccessAsync(period.WorkspaceId, ct))?.Role);

        var periodEntries = await entries.ListForPeriodAsync(period.WorkspaceId, period.UserId, period.PeriodStartUtc, period.PeriodEndUtc, ct);

        if (approve)
        {
            period.Approve(UserId, NewId(), comment, Now);
            foreach (var entry in periodEntries.Where(e => !e.IsRunning))
            {
                entry.Approve(UserId, Now);
            }

            Audit("time.timesheet_approved", "TimesheetPeriod", period.Id);
        }
        else
        {
            period.Reject(UserId, NewId(), comment, Now);
            foreach (var entry in periodEntries)
            {
                entry.Reject(Now);
            }

            Audit("time.timesheet_rejected", "TimesheetPeriod", period.Id, new { comment });
        }

        await SaveAsync(ct);
        return BuildDto(period, period.UserId, period.PeriodStartUtc, period.PeriodEndUtc, periodEntries);
    }

    public async Task<TimesheetDto> LockAsync(Guid periodId, CancellationToken ct = default)
    {
        var period = await timesheets.FindAsync(periodId, ct) ?? throw new NotFoundException("Timesheet not found.");
        TimeAuthorizer.EnsureManage((await AccessAsync(period.WorkspaceId, ct))?.Role);

        period.Lock(Now);
        var periodEntries = await entries.ListForPeriodAsync(period.WorkspaceId, period.UserId, period.PeriodStartUtc, period.PeriodEndUtc, ct);
        foreach (var entry in periodEntries.Where(e => !e.IsRunning))
        {
            entry.Lock(Now);
        }

        Audit("time.timesheet_locked", "TimesheetPeriod", period.Id);
        await SaveAsync(ct);
        return BuildDto(period, period.UserId, period.PeriodStartUtc, period.PeriodEndUtc, periodEntries);
    }

    private static TimesheetDto BuildDto(TimesheetPeriod? period, Guid userId, DateTimeOffset start, DateTimeOffset end, IReadOnlyList<TimeEntry> periodEntries)
    {
        var completed = periodEntries.Where(e => !e.IsRunning).ToList();
        var total = completed.Sum(e => e.DurationSeconds);
        var billable = completed.Where(e => e.IsBillable).Sum(e => e.DurationSeconds);
        var revenue = completed.Where(e => e.IsBillable).Sum(e => TimeMath.Amount(e.DurationSeconds, e.BillingRate));
        var cost = completed.Sum(e => TimeMath.Amount(e.DurationSeconds, e.CostRate));

        return new TimesheetDto(
            period?.Id ?? Guid.Empty, userId, start, end,
            period?.Status.ToString() ?? ApprovalStatus.Draft.ToString(),
            total, billable, revenue, cost, periodEntries.Select(TimeMapper.ToDto).ToList());
    }
}
