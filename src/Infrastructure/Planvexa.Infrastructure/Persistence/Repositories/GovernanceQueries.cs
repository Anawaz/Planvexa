namespace Planvexa.Infrastructure.Persistence.Repositories;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Audit.Domain;
using Planvexa.Modules.Chat.Domain;
using Planvexa.Modules.Collaboration.Domain;
using Planvexa.Modules.Documents.Domain;
using Planvexa.Modules.TimeTracking.Domain;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Governance;

/// <summary>
/// Implements the cross-module <see cref="IAuditQuery"/> over the append-only audit events. Audit events
/// carry a nullable workspace id (platform events have none); this filters explicitly by the requested
/// workspace so isolation holds even though the audit table is not workspace-query-filtered.
/// </summary>
internal sealed class AuditQuery(PlanvexaDbContext db) : IAuditQuery
{
    public async Task<IReadOnlyList<AuditRecord>> SearchAsync(Guid workspaceId, AuditFilter filter, CancellationToken cancellationToken = default)
    {
        var query = db.Set<AuditEvent>().Where(a => a.WorkspaceId == workspaceId);

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(a => a.Action == filter.Action);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            query = query.Where(a => a.EntityType == filter.EntityType);
        }

        if (filter.ActorUserId is { } actor)
        {
            query = query.Where(a => a.ActorUserId == actor);
        }

        if (filter.EntityId is { } entityId)
        {
            query = query.Where(a => a.EntityId == entityId);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(a => a.CreatedAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(a => a.CreatedAtUtc < to);
        }

        var max = filter.Max is > 0 and <= 5000 ? filter.Max : 500;

        var rows = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(max)
            .Select(a => new AuditRecord(a.Id, a.ActorUserId, a.Action, a.EntityType, a.EntityId, a.IpAddress, a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return rows;
    }
}

/// <summary>
/// Implements the cross-module <see cref="IExportDataSource"/> for governed exports. Produces flat CSV
/// rows for the supported datasets without the Governance module touching another module's tables. The
/// ambient workspace query filter provides isolation; the workspace id is also asserted explicitly.
/// </summary>
internal sealed class ExportDataSource(PlanvexaDbContext db) : IExportDataSource
{
    public async Task<ExportRows> GetRowsAsync(Guid workspaceId, string dataset, CancellationToken cancellationToken = default)
        => dataset switch
        {
            "audit" => await AuditRowsAsync(workspaceId, cancellationToken),
            "tasks" => await TaskRowsAsync(workspaceId, cancellationToken),
            _ => new ExportRows(Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>()),
        };

    private async Task<ExportRows> AuditRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "actorUserId", "action", "entityType", "entityId", "ipAddress", "createdAtUtc" };
        var events = await db.Set<AuditEvent>()
            .Where(a => a.WorkspaceId == workspaceId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(10000)
            .ToListAsync(ct);

        var rows = events
            .Select(a => (IReadOnlyList<string>)new[]
            {
                a.Id.ToString(),
                a.ActorUserId?.ToString() ?? string.Empty,
                a.Action,
                a.EntityType,
                a.EntityId?.ToString() ?? string.Empty,
                a.IpAddress ?? string.Empty,
                a.CreatedAtUtc.ToString("O"),
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    private async Task<ExportRows> TaskRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "listId", "spaceId", "title", "status", "priority", "isCompleted", "dueDate", "createdAtUtc" };
        var tasks = await db.Set<WorkItem>()
            .Where(t => t.WorkspaceId == workspaceId && !t.IsDeleted)
            .OrderBy(t => t.CreatedAtUtc)
            .Take(50000)
            .ToListAsync(ct);

        var rows = tasks
            .Select(t => (IReadOnlyList<string>)new[]
            {
                t.Id.ToString(),
                t.ListId.ToString(),
                t.SpaceId.ToString(),
                t.Title,
                t.StatusId.ToString(),
                t.Priority.ToString(),
                t.IsCompleted.ToString(),
                t.DueDate?.ToString("O") ?? string.Empty,
                t.CreatedAtUtc.ToString("O"),
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    /// <summary>
    /// Item 9: the "full" governed export — every workspace entity type as its own named CSV
    /// table, zipped up by ExportRunner. Row caps mirror the existing audit (10000) / tasks (50000)
    /// patterns above, sized per entity to bound worst-case memory; the point is to avoid an unbounded
    /// blowup on a huge workspace, not to guarantee completeness for one.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ExportRows>> GetFullWorkspaceArchiveAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => new Dictionary<string, ExportRows>(StringComparer.Ordinal)
        {
            ["spaces"] = await SpaceRowsAsync(workspaceId, cancellationToken),
            ["folders"] = await FolderRowsAsync(workspaceId, cancellationToken),
            ["lists"] = await ListRowsAsync(workspaceId, cancellationToken),
            ["tasks"] = await TaskRowsAsync(workspaceId, cancellationToken),
            ["comments"] = await CommentRowsAsync(workspaceId, cancellationToken),
            ["documents"] = await DocumentRowsAsync(workspaceId, cancellationToken),
            ["chat"] = await ChatRowsAsync(workspaceId, cancellationToken),
            ["timeEntries"] = await TimeEntryRowsAsync(workspaceId, cancellationToken),
            ["customFieldDefinitions"] = await CustomFieldDefinitionRowsAsync(workspaceId, cancellationToken),
            ["customFieldValues"] = await CustomFieldValueRowsAsync(workspaceId, cancellationToken),
        };

    private async Task<ExportRows> SpaceRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "name", "description", "position", "isArchived", "createdAtUtc" };
        var spaces = await db.Set<Space>()
            .Where(s => s.WorkspaceId == workspaceId && !s.IsDeleted)
            .OrderBy(s => s.Position)
            .Take(10000)
            .ToListAsync(ct);

        var rows = spaces
            .Select(s => (IReadOnlyList<string>)new[]
            {
                s.Id.ToString(),
                s.Name,
                s.Description ?? string.Empty,
                s.Position.ToString(CultureInfo.InvariantCulture),
                s.IsArchived.ToString(),
                s.CreatedAtUtc.ToString("O"),
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    private async Task<ExportRows> FolderRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "spaceId", "parentFolderId", "name", "position", "createdAtUtc" };
        var folders = await db.Set<Folder>()
            .Where(f => f.WorkspaceId == workspaceId && !f.IsDeleted)
            .OrderBy(f => f.Position)
            .Take(10000)
            .ToListAsync(ct);

        var rows = folders
            .Select(f => (IReadOnlyList<string>)new[]
            {
                f.Id.ToString(),
                f.SpaceId.ToString(),
                f.ParentFolderId?.ToString() ?? string.Empty,
                f.Name,
                f.Position.ToString(CultureInfo.InvariantCulture),
                f.CreatedAtUtc.ToString("O"),
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    private async Task<ExportRows> ListRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "spaceId", "folderId", "name", "statusSchemeId", "createdAtUtc" };
        var lists = await db.Set<TaskList>()
            .Where(l => l.WorkspaceId == workspaceId && !l.IsDeleted)
            .OrderBy(l => l.Position)
            .Take(10000)
            .ToListAsync(ct);

        var rows = lists
            .Select(l => (IReadOnlyList<string>)new[]
            {
                l.Id.ToString(),
                l.SpaceId.ToString(),
                l.FolderId?.ToString() ?? string.Empty,
                l.Name,
                l.StatusSchemeId.ToString(),
                l.CreatedAtUtc.ToString("O"),
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    private async Task<ExportRows> CommentRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "taskId", "parentId", "authorUserId", "body", "isEdited", "createdAtUtc" };
        var comments = await db.Set<Comment>()
            .Where(c => c.WorkspaceId == workspaceId && !c.IsDeleted)
            .OrderBy(c => c.CreatedAtUtc)
            .Take(50000)
            .ToListAsync(ct);

        var rows = comments
            .Select(c => (IReadOnlyList<string>)new[]
            {
                c.Id.ToString(),
                c.TaskId.ToString(),
                c.ParentId?.ToString() ?? string.Empty,
                c.AuthorUserId.ToString(),
                c.Body,
                c.IsEdited.ToString(),
                c.CreatedAtUtc.ToString("O"),
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    private async Task<ExportRows> DocumentRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "title", "content", "ownerUserId", "isPrivate", "spaceId", "listId", "taskId", "parentDocumentId", "createdAtUtc", "updatedAtUtc" };
        var documents = await db.Set<Document>()
            .Where(d => d.WorkspaceId == workspaceId)
            .OrderBy(d => d.CreatedAtUtc)
            .Take(10000)
            .ToListAsync(ct);

        var rows = documents
            .Select(d => (IReadOnlyList<string>)new[]
            {
                d.Id.ToString(),
                d.Title,
                d.Content,
                d.OwnerUserId.ToString(),
                d.IsPrivate.ToString(),
                d.SpaceId?.ToString() ?? string.Empty,
                d.ListId?.ToString() ?? string.Empty,
                d.TaskId?.ToString() ?? string.Empty,
                d.ParentDocumentId?.ToString() ?? string.Empty,
                d.CreatedAtUtc.ToString("O"),
                d.UpdatedAtUtc.ToString("O"),
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    private async Task<ExportRows> ChatRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "channelId", "parentMessageId", "authorUserId", "body", "createdAtUtc", "editedAtUtc" };
        var messages = await db.Set<ChatMessage>()
            .Where(m => m.WorkspaceId == workspaceId && !m.IsDeleted)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(50000)
            .ToListAsync(ct);

        var rows = messages
            .Select(m => (IReadOnlyList<string>)new[]
            {
                m.Id.ToString(),
                m.ChannelId.ToString(),
                m.ParentMessageId?.ToString() ?? string.Empty,
                m.AuthorUserId.ToString(),
                m.Body,
                m.CreatedAtUtc.ToString("O"),
                m.EditedAtUtc?.ToString("O") ?? string.Empty,
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    private async Task<ExportRows> TimeEntryRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "userId", "taskId", "startedAtUtc", "endedAtUtc", "durationSeconds", "description", "isBillable", "billingRate", "costRate", "approvalStatus" };
        var entries = await db.Set<TimeEntry>()
            .Where(e => e.WorkspaceId == workspaceId)
            .OrderBy(e => e.StartedAtUtc)
            .Take(50000)
            .ToListAsync(ct);

        var rows = entries
            .Select(e => (IReadOnlyList<string>)new[]
            {
                e.Id.ToString(),
                e.UserId.ToString(),
                e.TaskId?.ToString() ?? string.Empty,
                e.StartedAtUtc.ToString("O"),
                e.EndedAtUtc?.ToString("O") ?? string.Empty,
                e.DurationSeconds.ToString(CultureInfo.InvariantCulture),
                e.Description ?? string.Empty,
                e.IsBillable.ToString(),
                e.BillingRate.ToString(CultureInfo.InvariantCulture),
                e.CostRate.ToString(CultureInfo.InvariantCulture),
                e.ApprovalStatus.ToString(),
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    private async Task<ExportRows> CustomFieldDefinitionRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "scope", "scopeId", "name", "type", "isRequired", "position" };
        var definitions = await db.Set<CustomFieldDefinition>()
            .Where(d => d.WorkspaceId == workspaceId)
            .OrderBy(d => d.Position)
            .Take(10000)
            .ToListAsync(ct);

        var rows = definitions
            .Select(d => (IReadOnlyList<string>)new[]
            {
                d.Id.ToString(),
                d.Scope.ToString(),
                d.ScopeId?.ToString() ?? string.Empty,
                d.Name,
                d.Type.ToString(),
                d.IsRequired.ToString(),
                d.Position.ToString(CultureInfo.InvariantCulture),
            })
            .ToList();

        return new ExportRows(header, rows);
    }

    private async Task<ExportRows> CustomFieldValueRowsAsync(Guid workspaceId, CancellationToken ct)
    {
        var header = new[] { "id", "taskId", "definitionId", "textValue", "numberValue", "dateValue", "boolValue", "optionId", "userValue", "teamValue", "jsonValue", "updatedAtUtc" };
        var values = await db.Set<CustomFieldValue>()
            .Where(v => v.WorkspaceId == workspaceId)
            .OrderBy(v => v.UpdatedAtUtc)
            .Take(50000)
            .ToListAsync(ct);

        var rows = values
            .Select(v => (IReadOnlyList<string>)new[]
            {
                v.Id.ToString(),
                v.TaskId.ToString(),
                v.DefinitionId.ToString(),
                v.TextValue ?? string.Empty,
                v.NumberValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                v.DateValue?.ToString("O") ?? string.Empty,
                v.BoolValue?.ToString() ?? string.Empty,
                v.OptionId?.ToString() ?? string.Empty,
                v.UserValue?.ToString() ?? string.Empty,
                v.TeamValue?.ToString() ?? string.Empty,
                v.JsonValue ?? string.Empty,
                v.UpdatedAtUtc.ToString("O"),
            })
            .ToList();

        return new ExportRows(header, rows);
    }
}
