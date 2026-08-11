namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Collaboration.Domain;
using Planvexa.Modules.Integrations.Domain;
using Planvexa.Modules.Tenancy.Domain;
using Planvexa.Modules.TimeTracking.Domain;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.UserData;

/// <summary>
/// Implements <see cref="IUserDataQuery"/> and <see cref="IUserDataEraser"/> for the GDPR-style
/// user-data export/deletion flow. See those interfaces' doc comments for why every
/// call here runs on the maintenance connection: work.tasks, collab.comments, time.time_entries and
/// integrations.personal_access_tokens all FORCE row-level security keyed on the single ambient
/// Workspace, so spanning every Workspace the user belongs to — the whole point of this class — is not
/// possible over the normal RLS-enforced request connection.
/// </summary>
internal sealed class UserDataQuery(PlanvexaDbContext db, MaintenanceConnection maintenance)
    : IUserDataQuery, IUserDataEraser
{
    public Task<IReadOnlyList<UserWorkspaceMembership>> GetMembershipsAsync(Guid userId, CancellationToken ct = default)
        => maintenance.LookupAsync(db, async () =>
        {
            var memberships = await db.Set<WorkspaceMember>().IgnoreQueryFilters()
                .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
                .ToListAsync(ct);

            if (memberships.Count == 0)
            {
                return (IReadOnlyList<UserWorkspaceMembership>)Array.Empty<UserWorkspaceMembership>();
            }

            var workspaceIds = memberships.Select(m => m.WorkspaceId).ToList();
            var workspaceNames = await db.Set<Workspace>().IgnoreQueryFilters()
                .Where(w => workspaceIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name, ct);

            var activeOwnerCounts = await db.Set<WorkspaceMember>().IgnoreQueryFilters()
                .Where(m => workspaceIds.Contains(m.WorkspaceId) && m.Role == MembershipRole.Owner && m.Status == MembershipStatus.Active)
                .GroupBy(m => m.WorkspaceId)
                .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, ct);

            return (IReadOnlyList<UserWorkspaceMembership>)memberships
                .Select(m => new UserWorkspaceMembership(
                    m.WorkspaceId,
                    workspaceNames.GetValueOrDefault(m.WorkspaceId, string.Empty),
                    m.Role.ToString(),
                    m.JoinedAtUtc,
                    IsSoleActiveOwner: m.Role == MembershipRole.Owner && activeOwnerCounts.GetValueOrDefault(m.WorkspaceId) <= 1))
                .ToList();
        });

    public Task<IReadOnlyList<UserTaskRecord>> GetTasksAsync(Guid userId, CancellationToken ct = default)
        => maintenance.LookupAsync(db, async () =>
        {
            var created = await db.Set<WorkItem>().IgnoreQueryFilters()
                .Where(t => t.CreatedByUserId == userId)
                .Select(t => new UserTaskRecord(t.Id, t.WorkspaceId, t.Title, "Created", t.CreatedAtUtc, t.IsDeleted))
                .ToListAsync(ct);

            var assignedTaskIds = await db.Set<TaskAssignee>().IgnoreQueryFilters()
                .Where(a => a.UserId == userId)
                .Select(a => a.TaskId)
                .ToListAsync(ct);

            var assigned = new List<UserTaskRecord>();
            if (assignedTaskIds.Count > 0)
            {
                assigned = await db.Set<WorkItem>().IgnoreQueryFilters()
                    .Where(t => assignedTaskIds.Contains(t.Id))
                    .Select(t => new UserTaskRecord(t.Id, t.WorkspaceId, t.Title, "Assigned", t.CreatedAtUtc, t.IsDeleted))
                    .ToListAsync(ct);
            }

            // A task the user both created AND is assigned to appears once per relationship — that is
            // intentional (the export is meant to show both facts), not a duplicate to collapse.
            return (IReadOnlyList<UserTaskRecord>)created.Concat(assigned).ToList();
        });

    public Task<IReadOnlyList<UserCommentRecord>> GetCommentsAsync(Guid userId, CancellationToken ct = default)
        => maintenance.LookupAsync(db, async () =>
        {
            var rows = await db.Set<Comment>().IgnoreQueryFilters()
                .Where(c => c.AuthorUserId == userId)
                .Select(c => new UserCommentRecord(c.Id, c.WorkspaceId, c.TaskId, c.Body, c.CreatedAtUtc, c.IsDeleted))
                .ToListAsync(ct);
            return (IReadOnlyList<UserCommentRecord>)rows;
        });

    public Task<IReadOnlyList<UserTimeEntryRecord>> GetTimeEntriesAsync(Guid userId, CancellationToken ct = default)
        => maintenance.LookupAsync(db, async () =>
        {
            var rows = await db.Set<TimeEntry>().IgnoreQueryFilters()
                .Where(t => t.UserId == userId)
                .Select(t => new UserTimeEntryRecord(t.Id, t.WorkspaceId, t.TaskId, t.StartedAtUtc, t.EndedAtUtc, t.DurationSeconds, t.Description))
                .ToListAsync(ct);
            return (IReadOnlyList<UserTimeEntryRecord>)rows;
        });

    public Task<int> DeletePersonalAccessTokensAsync(Guid userId, CancellationToken ct = default)
        => maintenance.LookupAsync(db, async () =>
        {
            var tokens = await db.Set<PersonalAccessToken>().IgnoreQueryFilters()
                .Where(p => p.UserId == userId)
                .ToListAsync(ct);

            if (tokens.Count == 0)
            {
                return 0;
            }

            db.Set<PersonalAccessToken>().RemoveRange(tokens);
            await db.SaveChangesAsync(ct);
            return tokens.Count;
        });
}
