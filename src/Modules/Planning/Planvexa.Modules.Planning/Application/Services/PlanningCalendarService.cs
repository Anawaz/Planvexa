namespace Planvexa.Modules.Planning.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Planning.Authorization;
using Planvexa.Modules.Planning.Domain;

/// <summary>Manages the workspace working calendar: work schedule, holidays, and per-user leave.</summary>
public sealed class PlanningCalendarService(
    PlanningServiceContext ctx,
    IWorkScheduleStore schedules,
    IHolidayStore holidays,
    ILeaveStore leave)
    : PlanningServiceBase(ctx)
{
    public async Task<WorkScheduleDto> GetScheduleAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var schedule = await schedules.FindAsync(workspaceId, ct);
        if (schedule is null)
        {
            schedule = WorkSchedule.CreateDefault(NewId(), workspaceId);
        }

        return new WorkScheduleDto(schedule.WorkingDays(), schedule.DailyCapacityHours);
    }

    public async Task<WorkScheduleDto> UpdateScheduleAsync(UpdateWorkScheduleCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var schedule = await schedules.FindAsync(workspaceId, ct);
        if (schedule is null)
        {
            schedule = WorkSchedule.CreateDefault(NewId(), workspaceId);
            schedule.Update(command.WorkingDays, command.DailyCapacityHours);
            schedules.Add(schedule);
        }
        else
        {
            schedule.Update(command.WorkingDays, command.DailyCapacityHours);
        }

        Audit("planning.schedule.updated", "WorkSchedule", schedule.Id, new { command.WorkingDays, command.DailyCapacityHours });
        await SaveAsync(ct);
        return new WorkScheduleDto(schedule.WorkingDays(), schedule.DailyCapacityHours);
    }

    public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var list = await holidays.ListByWorkspaceAsync(workspaceId, ct);
        return list.Select(h => new HolidayDto(h.Id, new DateTimeOffset(h.DateUtc, TimeSpan.Zero), h.Name)).ToList();
    }

    public async Task<HolidayDto> AddHolidayAsync(AddHolidayCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var holiday = Holiday.Create(NewId(), workspaceId, command.DateUtc, command.Name);
        holidays.Add(holiday);
        Audit("planning.holiday.added", "Holiday", holiday.Id, new { holiday.DateUtc, holiday.Name });
        await SaveAsync(ct);
        return new HolidayDto(holiday.Id, new DateTimeOffset(holiday.DateUtc, TimeSpan.Zero), holiday.Name);
    }

    public async Task RemoveHolidayAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        PlanningAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var holiday = await holidays.FindAsync(id, ct)
            ?? throw new NotFoundException("Holiday not found.");
        holidays.Remove(holiday);
        Audit("planning.holiday.removed", "Holiday", id);
        await SaveAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveDto>> ListLeaveAsync(Guid? userId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        PlanningAuthorizer.EnsureRead(role);

        var list = await leave.ListByWorkspaceAsync(workspaceId, userId, ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<LeaveDto> AddLeaveAsync(AddLeaveCommand command, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        PlanningAuthorizer.EnsureEditContent(role);

        var targetUser = command.UserId ?? UserId;
        if (targetUser != UserId)
        {
            // Recording leave for someone else is an administrative action.
            PlanningAuthorizer.EnsureManage(role);
        }

        var type = Enum.TryParse<LeaveType>(command.Type, ignoreCase: true, out var parsed) ? parsed : LeaveType.Other;
        var entry = LeaveEntry.Create(NewId(), workspaceId, targetUser, command.StartUtc, command.EndUtc, type);
        leave.Add(entry);
        Audit("planning.leave.added", "LeaveEntry", entry.Id, new { entry.UserId, entry.StartDate, entry.EndDate, Type = type.ToString() });
        await SaveAsync(ct);
        return ToDto(entry);
    }

    public async Task RemoveLeaveAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        PlanningAuthorizer.EnsureEditContent(role);

        var entry = await leave.FindAsync(id, ct)
            ?? throw new NotFoundException("Leave entry not found.");
        if (entry.UserId != UserId)
        {
            PlanningAuthorizer.EnsureManage(role);
        }

        leave.Remove(entry);
        Audit("planning.leave.removed", "LeaveEntry", id);
        await SaveAsync(ct);
    }

    private static LeaveDto ToDto(LeaveEntry e)
        => new(e.Id, e.UserId, new DateTimeOffset(e.StartDate, TimeSpan.Zero), new DateTimeOffset(e.EndDate, TimeSpan.Zero), e.Type.ToString());
}
