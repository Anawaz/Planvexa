namespace Planvexa.Infrastructure.HostAdmin;

using Microsoft.EntityFrameworkCore;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.Infrastructure.Persistence;
using Planvexa.Infrastructure.Persistence.Repositories;
using Planvexa.Modules.Audit.Domain;
using Planvexa.Modules.Documents.Domain;
using Planvexa.Modules.Identity.Domain;
using Planvexa.Modules.Tenancy.Domain;
using Planvexa.Modules.WorkManagement.Domain;

/// <summary>
/// Cross-Workspace reads for the host administration console.
///
/// This lives in Infrastructure rather than in a module because it deliberately spans several of them
/// (Tenancy workspaces/members, Identity users, Audit events) and the modular-monolith rule forbids a
/// module referencing another module (<c>tests/Architecture/ModuleBoundaryTests</c>). Infrastructure
/// is the layer that already owns the single <see cref="PlanvexaDbContext"/> and reaches across every
/// module — <c>WorkspaceResolver</c> in this same project does exactly this.
///
/// Two rules every query here follows:
/// <list type="number">
/// <item>Wrapped in <see cref="HostAdminSession.WithoutWorkspaceAsync"/>, so the ambient workspace is
/// provably empty and <c>app.current_user</c> is provably the caller — that is what the host-admin RLS
/// policies (script 0094) key on.</item>
/// <item><c>IgnoreQueryFilters()</c> on workspace-owned sets, because EF's C#-side filter would
/// otherwise reduce everything to the (empty) ambient workspace. RLS, not the EF filter, is the
/// authorization boundary on this path.</item>
/// </list>
///
/// METADATA AND AGGREGATES ONLY. Nothing here reads task titles, document bodies, comments or chat
/// messages, and nothing here should start to: the host administrator manages the installation, not
/// the work inside it. See <see cref="HostWorkspaceUsage"/> — counts and bytes, never content.
/// </summary>
public sealed class HostAdminQueries(PlanvexaDbContext db, ICurrentUser currentUser, IClock clock)
{
    private const int MaxPageSize = 200;

    /// <summary>Ceiling on a single CSV export — see <see cref="ExportActivityCsvAsync"/>.</summary>
    private const int MaxExportRows = 10_000;

    public Task<HostOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
        => Read(async () =>
        {
            var now = clock.UtcNow;
            var workspaces = db.Workspaces.IgnoreQueryFilters();
            var members = db.WorkspaceMembers.IgnoreQueryFilters();

            var activeWorkspaces = await workspaces.CountAsync(w => w.Status == WorkspaceStatus.Active, cancellationToken);
            var archivedWorkspaces = await workspaces.CountAsync(w => w.Status == WorkspaceStatus.Archived, cancellationToken);
            var activeUsers = await db.Users.CountAsync(u => u.IsActive, cancellationToken);
            var disabledUsers = await db.Users.CountAsync(u => !u.IsActive, cancellationToken);
            var hostAdmins = await db.Users.CountAsync(u => u.IsHostAdmin && u.IsActive, cancellationToken);
            var membershipCount = await members.CountAsync(cancellationToken);

            var sevenDaysAgo = now.AddDays(-7);
            var thirtyDaysAgo = now.AddDays(-30);
            var seen7 = await db.Users.CountAsync(u => u.LastSeenAtUtc >= sevenDaysAgo, cancellationToken);
            var seen30 = await db.Users.CountAsync(u => u.LastSeenAtUtc >= thirtyDaysAgo, cancellationToken);

            // Twelve buckets, grouped in the database. The cutoff is the first of the month 11 months
            // back so the current (partial) month is the twelfth bucket.
            var trendFrom = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-11);
            var trend = await workspaces
                .Where(w => w.CreatedAtUtc >= trendFrom)
                .GroupBy(w => new { w.CreatedAtUtc.Year, w.CreatedAtUtc.Month })
                .Select(g => new HostMonthlyCount(g.Key.Year, g.Key.Month, g.Count()))
                .ToListAsync(cancellationToken);

            var recent = await ReadActivityAsync(action: null, entityType: null, actorUserId: null,
                workspaceId: null, from: null, to: null, skip: 0, take: 20, cancellationToken);

            return new HostOverview(
                activeWorkspaces, archivedWorkspaces, activeUsers, disabledUsers, hostAdmins, membershipCount,
                seen7, seen30,
                trend.OrderBy(t => t.Year).ThenBy(t => t.Month).ToList(),
                recent.Items);
        }, cancellationToken);

    public Task<HostPage<HostWorkspaceSummary>> ListWorkspacesAsync(
        string? search, string? status, int skip, int take, CancellationToken cancellationToken = default)
        => Read(async () =>
        {
            var query = db.Workspaces.IgnoreQueryFilters();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = $"%{search.Trim()}%";
                query = query.Where(w => EF.Functions.ILike(w.Name, term) || EF.Functions.ILike(w.Slug, term));
            }

            if (Enum.TryParse<WorkspaceStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                query = query.Where(w => w.Status == parsedStatus);
            }

            var total = await query.CountAsync(cancellationToken);
            var page = await query
                .OrderByDescending(w => w.CreatedAtUtc)
                .Skip(Math.Max(0, skip))
                .Take(Clamp(take))
                .ToListAsync(cancellationToken);

            return new HostPage<HostWorkspaceSummary>(
                await ToSummariesAsync(page, cancellationToken), total);
        }, cancellationToken);

    public Task<HostWorkspaceDetail?> GetWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => Read(async () =>
        {
            var workspace = await db.Workspaces.IgnoreQueryFilters()
                .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
            if (workspace is null)
            {
                return null;
            }

            var summary = (await ToSummariesAsync([workspace], cancellationToken))[0];

            var features = await db.FeatureEntitlements.IgnoreQueryFilters()
                .Where(f => f.WorkspaceId == workspaceId && f.IsEnabled)
                .Select(f => f.FeatureKey)
                .OrderBy(key => key)
                .ToListAsync(cancellationToken);

            var memberRows = await db.WorkspaceMembers.IgnoreQueryFilters()
                .Where(m => m.WorkspaceId == workspaceId)
                .OrderBy(m => m.JoinedAtUtc)
                .ToListAsync(cancellationToken);

            var profiles = await ProfilesForAsync(memberRows.Select(m => m.UserId), cancellationToken);
            var members = memberRows
                .Select(m => new HostWorkspaceMember(
                    m.Id, m.UserId,
                    profiles.GetValueOrDefault(m.UserId)?.DisplayName,
                    profiles.GetValueOrDefault(m.UserId)?.Email,
                    m.Role.ToString(), m.Status.ToString(), m.IsGuest, m.JoinedAtUtc))
                .ToList();

            return new HostWorkspaceDetail(summary, features, members);
        }, cancellationToken);

    /// <summary>
    /// Content volume for one workspace. Unlike everything else in this class this runs under the
    /// TARGET workspace's ambient context rather than none, because work/docs tables keep only their
    /// strict <c>workspace_isolation</c> policy (0029) — giving each of them a host-admin policy would
    /// mean touching dozens of tables to produce six numbers.
    /// </summary>
    public Task<HostWorkspaceUsage> GetWorkspaceUsageAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => TenancySessionGuard.WithStampedWorkspaceAsync(db, workspaceId, async () =>
        {
            var spaces = await db.Set<Space>().IgnoreQueryFilters()
                .CountAsync(s => s.WorkspaceId == workspaceId && !s.IsDeleted, cancellationToken);
            var lists = await db.Set<TaskList>().IgnoreQueryFilters()
                .CountAsync(l => l.WorkspaceId == workspaceId && !l.IsDeleted, cancellationToken);
            var tasks = await db.Set<WorkItem>().IgnoreQueryFilters()
                .CountAsync(t => t.WorkspaceId == workspaceId && !t.IsDeleted, cancellationToken);
            var documents = await db.Set<Document>().IgnoreQueryFilters()
                .CountAsync(d => d.WorkspaceId == workspaceId, cancellationToken);

            var attachments = db.Set<TaskAttachment>().IgnoreQueryFilters()
                .Where(a => a.WorkspaceId == workspaceId);
            var attachmentCount = await attachments.CountAsync(cancellationToken);
            // SumAsync over an empty set throws on a non-nullable projection; project to long? instead.
            var attachmentBytes = await attachments.SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0L;

            return new HostWorkspaceUsage(workspaceId, spaces, lists, tasks, documents, attachmentCount, attachmentBytes);
        }, cancellationToken);

    public Task<HostPage<HostUserSummary>> ListUsersAsync(
        string? search, string? status, int skip, int take, CancellationToken cancellationToken = default)
        => Read(async () =>
        {
            var query = db.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = $"%{search.Trim()}%";
                query = query.Where(u => EF.Functions.ILike(u.Email, term) || EF.Functions.ILike(u.DisplayName, term));
            }

            query = status?.Trim().ToLowerInvariant() switch
            {
                "active" => query.Where(u => u.IsActive),
                "disabled" => query.Where(u => !u.IsActive),
                "hostadmin" or "host-admin" => query.Where(u => u.IsHostAdmin),
                _ => query,
            };

            var total = await query.CountAsync(cancellationToken);
            var page = await query
                .OrderByDescending(u => u.CreatedAtUtc)
                .Skip(Math.Max(0, skip))
                .Take(Clamp(take))
                .ToListAsync(cancellationToken);

            var ids = page.Select(u => u.Id).ToList();
            var counts = await db.WorkspaceMembers.IgnoreQueryFilters()
                .Where(m => ids.Contains(m.UserId))
                .GroupBy(m => m.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

            return new HostPage<HostUserSummary>(
                page.Select(u => ToSummary(u, counts.GetValueOrDefault(u.Id))).ToList(), total);
        }, cancellationToken);

    public Task<HostUserDetail?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => Read(async () =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return null;
            }

            // Ordered on the joined column BEFORE projecting: ordering by a property of an already
            // constructed record is not translatable (the projection is not a queryable shape any more).
            var memberships = await db.WorkspaceMembers.IgnoreQueryFilters()
                .Where(m => m.UserId == userId)
                .Join(
                    db.Workspaces.IgnoreQueryFilters(),
                    m => m.WorkspaceId,
                    w => w.Id,
                    (m, w) => new { Member = m, Workspace = w })
                .OrderBy(x => x.Workspace.Name)
                .Select(x => new HostUserMembership(
                    x.Workspace.Id, x.Workspace.Name, x.Workspace.Slug, x.Workspace.Status.ToString(),
                    x.Member.Role.ToString(), x.Member.Status.ToString(), x.Member.JoinedAtUtc))
                .ToListAsync(cancellationToken);

            return new HostUserDetail(ToSummary(user, memberships.Count), memberships);
        }, cancellationToken);

    public Task<HostPage<HostActivityEntry>> ListActivityAsync(
        string? action, string? entityType, Guid? actorUserId, Guid? workspaceId,
        DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken cancellationToken = default)
        => Read(() => ReadActivityAsync(action, entityType, actorUserId, workspaceId, from, to, skip, take, cancellationToken),
            cancellationToken);

    /// <summary>
    /// The same filtered activity as <see cref="ListActivityAsync"/>, as CSV for offline review or an
    /// external SIEM. Capped at <see cref="MaxExportRows"/> — an unbounded export of a table that grows
    /// forever would build the whole thing in memory. The row count is in the header line so a
    /// truncated export announces itself rather than looking complete.
    /// </summary>
    public Task<string> ExportActivityCsvAsync(
        string? action, string? entityType, Guid? actorUserId, Guid? workspaceId,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default)
        => Read(async () =>
        {
            var page = await ReadActivityAsync(
                action, entityType, actorUserId, workspaceId, from, to, 0, MaxExportRows, cancellationToken,
                maxTake: MaxExportRows);

            return HostAdminCsv.Write(
                ["When (UTC)", "Action", "Entity type", "Entity id", "Actor", "Actor id", "Workspace", "Workspace id", "IP"],
                page.Items.Select(IReadOnlyList<string> (e) =>
                [
                    e.CreatedAtUtc.UtcDateTime.ToString("O"),
                    e.Action,
                    e.EntityType,
                    e.EntityId?.ToString() ?? string.Empty,
                    e.ActorDisplayName ?? "System",
                    e.ActorUserId?.ToString() ?? string.Empty,
                    // An event with no workspace is instance-level (an account disabled, settings changed).
                    e.WorkspaceName ?? "Instance",
                    e.WorkspaceId?.ToString() ?? string.Empty,
                    e.IpAddress ?? string.Empty,
                ]));
        }, cancellationToken);

    // ---- internals ----

    /// <summary>
    /// Every read goes through here: empty ambient workspace + stamped caller, on one held-open
    /// connection. See <see cref="HostAdminSession"/>.
    /// </summary>
    private Task<T> Read<T>(Func<Task<T>> read, CancellationToken cancellationToken)
        => HostAdminSession.WithoutWorkspaceAsync(db, currentUser.UserId, read, cancellationToken);

    private async Task<HostPage<HostActivityEntry>> ReadActivityAsync(
        string? action, string? entityType, Guid? actorUserId, Guid? workspaceId,
        DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken cancellationToken,
        int maxTake = MaxPageSize)
    {
        // audit.audit_events' audit_isolation policy (0029) is the lenient form — with no ambient
        // workspace every row is visible, which is precisely the instance-wide view wanted here.
        var query = db.AuditEvents.IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(action))
        {
            var term = $"%{action.Trim()}%";
            query = query.Where(e => EF.Functions.ILike(e.Action, term));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(e => e.EntityType == entityType);
        }

        if (actorUserId is { } actor)
        {
            query = query.Where(e => e.ActorUserId == actor);
        }

        if (workspaceId is { } workspace)
        {
            query = query.Where(e => e.WorkspaceId == workspace);
        }

        if (from is { } fromUtc)
        {
            query = query.Where(e => e.CreatedAtUtc >= fromUtc);
        }

        if (to is { } toUtc)
        {
            query = query.Where(e => e.CreatedAtUtc <= toUtc);
        }

        var total = await query.CountAsync(cancellationToken);
        var page = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip(Math.Max(0, skip))
            .Take(Clamp(take, maxTake))
            .ToListAsync(cancellationToken);

        var actors = await ProfilesForAsync(page.Select(e => e.ActorUserId).OfType<Guid>(), cancellationToken);
        var workspaceIds = page.Select(e => e.WorkspaceId).OfType<Guid>().Distinct().ToList();
        var workspaceNames = await db.Workspaces.IgnoreQueryFilters()
            .Where(w => workspaceIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name, cancellationToken);

        var items = page
            .Select(e => new HostActivityEntry(
                e.Id, e.CreatedAtUtc, e.Action, e.EntityType, e.EntityId, e.ActorUserId,
                e.ActorUserId is { } id ? actors.GetValueOrDefault(id)?.DisplayName : null,
                e.WorkspaceId,
                e.WorkspaceId is { } wid ? workspaceNames.GetValueOrDefault(wid) : null,
                e.IpAddress))
            .ToList();

        return new HostPage<HostActivityEntry>(items, total);
    }

    private async Task<IReadOnlyList<HostWorkspaceSummary>> ToSummariesAsync(
        IReadOnlyList<Workspace> workspaces, CancellationToken cancellationToken)
    {
        if (workspaces.Count == 0)
        {
            return [];
        }

        var ids = workspaces.Select(w => w.Id).ToList();

        var memberCounts = await db.WorkspaceMembers.IgnoreQueryFilters()
            .Where(m => ids.Contains(m.WorkspaceId))
            .GroupBy(m => m.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, cancellationToken);

        var lastActivity = await db.AuditEvents.IgnoreQueryFilters()
            .Where(e => e.WorkspaceId != null && ids.Contains(e.WorkspaceId!.Value))
            .GroupBy(e => e.WorkspaceId!.Value)
            .Select(g => new { WorkspaceId = g.Key, Last = g.Max(e => e.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Last, cancellationToken);

        // CreatedByUserId is the founder, which is not necessarily the CURRENT Owner (ownership can be
        // transferred). Resolve the live Owner membership and fall back to the founder only when there
        // is none — an Owner-less workspace is possible if the Owner's account was anonymized.
        // Grouped in memory, not in SQL: PostgreSQL has no min() over uuid, so a GroupBy(...).Min(UserId)
        // would fail to translate. A workspace has one Owner in practice, so this list is ~one row each.
        var ownerRows = await db.WorkspaceMembers.IgnoreQueryFilters()
            .Where(m => ids.Contains(m.WorkspaceId)
                && m.Role == MembershipRole.Owner
                && m.Status == MembershipStatus.Active)
            .OrderBy(m => m.JoinedAtUtc)
            .Select(m => new { m.WorkspaceId, m.UserId })
            .ToListAsync(cancellationToken);

        var owners = ownerRows
            .GroupBy(m => m.WorkspaceId)
            .ToDictionary(g => g.Key, g => g.First().UserId);

        var ownerIds = workspaces
            .Select(w => owners.TryGetValue(w.Id, out var owner) ? owner : w.CreatedByUserId)
            .Distinct();
        var profiles = await ProfilesForAsync(ownerIds, cancellationToken);

        return workspaces.Select(w =>
        {
            var ownerId = owners.TryGetValue(w.Id, out var owner) ? owner : w.CreatedByUserId;
            var profile = profiles.GetValueOrDefault(ownerId);
            return new HostWorkspaceSummary(
                w.Id, w.Name, w.Slug, w.Status.ToString(), w.CreatedAtUtc,
                profile is null ? null : ownerId, profile?.DisplayName, profile?.Email,
                memberCounts.GetValueOrDefault(w.Id),
                lastActivity.TryGetValue(w.Id, out var last) ? last : null);
        }).ToList();
    }

    /// <summary>
    /// Batched user lookup. Not <c>IUserDirectory.FindByIdAsync</c> in a loop: that is the N+1 shape
    /// the members endpoint already carries a ponytail note about, and a host list page can easily
    /// reference a hundred distinct users at once.
    /// </summary>
    private async Task<Dictionary<Guid, User>> ProfilesForAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        return ids.Count == 0
            ? []
            : await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);
    }

    private static HostUserSummary ToSummary(User user, int workspaceCount)
        => new(user.Id, user.Email, user.DisplayName, user.IsActive, user.IsHostAdmin, user.IsAnonymized,
            user.CreatedAtUtc, user.LastSeenAtUtc, workspaceCount);

    /// <summary>
    /// Bounds a caller-supplied page size. <paramref name="max"/> is the API page ceiling by default;
    /// the CSV export raises it to <see cref="MaxExportRows"/>, which is the one place a larger read is
    /// intentional.
    /// </summary>
    private static int Clamp(int take, int max = MaxPageSize) => take <= 0 ? 50 : Math.Min(take, max);
}
