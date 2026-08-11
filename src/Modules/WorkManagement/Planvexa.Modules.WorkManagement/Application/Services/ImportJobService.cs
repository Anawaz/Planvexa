namespace Planvexa.Modules.WorkManagement.Application.Services;

using System.Text.Json;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Application.Importers;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Users;
using Planvexa.SharedContracts.Work;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Runs a bulk data import end to end: upload -> (optional column mapping) -> validate -> commit
/// (). Lives in WorkManagement rather than a separate module because every write it
/// makes needs the SAME authorization gate manual creation goes through — <see cref="SpaceService.CreateAsync"/>
/// and <see cref="TaskListService.CreateAsync"/> (both already require ManageStructure on their parent),
/// plus this class's own <see cref="WorkServiceBase.EnsureEditContentAsync"/> check on the target List
/// before every task write, exactly like <c>WorkItemService.CreateAsync</c> does. Task-level writes
/// themselves go through <see cref="ITaskWriteApi"/> — the same approved contract Forms/Automations use
/// (AGENTS.md rule 7's "approved contract", here used intra-module rather than cross-module, which is
/// still the correct reuse: one write surface, one place idempotency/authorization intent is documented).
///
/// Runs under the ambient workspace of the REAL authenticated user who started the import (never a
/// system actor) — importers "write bulk data on the user's behalf" per the security brief, so
/// every Space/List/Task it creates is attributed to, and gated by the permissions of, that user.
///
/// Resumable (AGENTS.md rule 13): <see cref="CommitAsync"/> only processes rows still
/// <see cref="ImportRowStatus.Valid"/> (not yet <see cref="ImportRowStatus.Committed"/>), and commits one
/// row at a time with its own SaveChanges — an interrupted commit (e.g. app restart) leaves already
/// committed rows as <c>Committed</c>, and re-invoking commit picks up exactly where it left off with no
/// duplicate tasks. Space/List find-or-create is idempotent by (workspace, name) lookup rather than an
/// in-memory cache, so it survives a restart the same way.
/// </summary>
public sealed class ImportJobService(
    WorkServiceContext ctx,
    IImportJobStore jobs,
    IImportJobRowStore jobRows,
    ISpaceStore spaces,
    ITaskListStore lists,
    SpaceService spaceService,
    TaskListService listService,
    ITaskWriteApi taskWrite,
    IWorkspaceRosterQuery roster,
    IUserDirectory users,
    IEnumerable<IImportSource> importSources)
    : WorkServiceBase(ctx)
{
    private const long MaxUploadBytes = 10L * 1024 * 1024;

    private readonly IReadOnlyDictionary<string, IImportSource> sourcesByType =
        importSources.ToDictionary(s => s.SourceType, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> SupportedSourceTypes => sourcesByType.Keys.ToList();

    public async Task<ImportJobDto> UploadAsync(
        string sourceType, string fileName, Stream content, long sizeBytes,
        string? targetSpaceName, string? targetListName, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);

        if (sizeBytes <= 0)
        {
            throw new ValidationAppException("The uploaded file is empty.");
        }

        if (sizeBytes > MaxUploadBytes)
        {
            throw new ValidationAppException($"Import files are limited to {MaxUploadBytes / (1024 * 1024)} MB.");
        }

        if (!sourcesByType.TryGetValue(sourceType, out var source))
        {
            throw new ValidationAppException($"Unknown or unsupported import source type '{sourceType}'.");
        }

        ParsedImportSource parsed;
        try
        {
            parsed = source.Parse(content);
        }
        catch (NotSupportedException ex)
        {
            throw new ValidationAppException(ex.Message);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            throw new ValidationAppException($"Could not parse the uploaded file: {ex.Message}");
        }

        var job = ImportJob.Create(
            NewId(), workspaceId, source.SourceType, fileName, targetSpaceName, targetListName,
            targetSpaceId: null, targetListId: null, UserId, Now);
        job.SetDetectedColumns(parsed.DetectedColumns, Now);
        job.SetTotalRows(parsed.Rows.Count, Now);
        job.SetColumnMapping(
            parsed.SuggestedMapping is { Count: > 0 } mapping ? JsonSerializer.Serialize(mapping) : null, Now);
        jobs.Add(job);

        for (var i = 0; i < parsed.Rows.Count; i++)
        {
            jobRows.Add(ImportJobRow.Create(NewId(), workspaceId, job.Id, i, JsonSerializer.Serialize(parsed.Rows[i])));
        }

        Audit("work.import_job.uploaded", nameof(ImportJob), job.Id, new { job.SourceType, job.FileName, job.TotalRows });
        await SaveAsync(ct);
        return await ToDtoAsync(job, ct);
    }

    public async Task<ImportJobDto> SetMappingAsync(Guid jobId, IReadOnlyDictionary<string, string> mapping, CancellationToken ct)
    {
        var job = await LoadAsync(jobId, ct);
        job.SetColumnMapping(JsonSerializer.Serialize(mapping), Now);
        await SaveAsync(ct);
        return await ToDtoAsync(job, ct);
    }

    /// <summary>Normalizes every row not yet Valid/Committed against the current column mapping,
    /// reporting per-row errors before anything is written — re-runnable any number of times (e.g. after
    /// the mapping changes) with no side effects on already-committed rows.</summary>
    public async Task<ImportJobDto> ValidateAsync(Guid jobId, CancellationToken ct)
    {
        var job = await LoadAsync(jobId, ct);
        var mapping = DeserializeMapping(job.ColumnMappingJson);

        var pending = await jobRows.ListPendingOrInvalidAsync(job.Id, ct);
        foreach (var row in pending)
        {
            var rawFields = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawFieldsJson) ?? new();
            var normalized = ImportRowNormalizer.Normalize(rawFields, mapping, out var error);
            if (normalized is null)
            {
                row.MarkInvalid(error ?? "Row failed validation.");
            }
            else
            {
                row.MarkValid();
            }
        }

        var allRows = await jobRows.ListByJobAsync(job.Id, ct);
        var errorCount = allRows.Count(r => r.Status == ImportRowStatus.Invalid);
        job.RecordValidation(errorCount, Now);

        Audit("work.import_job.validated", nameof(ImportJob), job.Id, new { job.TotalRows, errorCount });
        await SaveAsync(ct);
        return await ToDtoAsync(job, ct);
    }

    /// <summary>Commits every row still Valid (not yet Committed) — safe to call again after an
    /// interruption; rows already Committed are skipped entirely (AGENTS.md rule 13).</summary>
    public async Task<ImportJobDto> CommitAsync(Guid jobId, CancellationToken ct)
    {
        var job = await LoadAsync(jobId, ct);
        var mapping = DeserializeMapping(job.ColumnMappingJson);

        job.BeginCommit(Now);
        await SaveAsync(ct);

        var toCommit = await jobRows.ListValidNotCommittedAsync(job.Id, ct);
        var committedSoFar = job.ProcessedRows;
        var spaceIdByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var listIdByKey = new Dictionary<(Guid SpaceId, string Name), Guid>();
        var assigneeIdByIdentifier = await BuildAssigneeLookupAsync(job.WorkspaceId, ct);

        foreach (var row in toCommit)
        {
            var rawFields = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawFieldsJson) ?? new();
            var normalized = ImportRowNormalizer.Normalize(rawFields, mapping, out var error);
            if (normalized is null)
            {
                // The mapping changed since validation made this row Valid — treat as a commit-time
                // failure rather than crash the whole batch.
                row.MarkInvalid(error ?? "Row failed validation.");
                await SaveAsync(ct);
                continue;
            }

            try
            {
                var listId = await ResolveTargetListAsync(
                    job, normalized.SpaceName, normalized.ListName, spaceIdByName, listIdByKey, ct);

                var list = await lists.FindAsync(listId, ct) ?? throw new NotFoundException("Target list not found.");
                await EnsureEditContentAsync(list, WorkResourceTypes.List, ct);

                var taskId = await taskWrite.CreateTaskAsync(listId, normalized.Title, normalized.Description, ct)
                    ?? throw new InvalidOperationException("Task creation returned no id for an existing list.");

                if (!string.IsNullOrWhiteSpace(normalized.PriorityName))
                {
                    await taskWrite.SetPriorityByNameAsync(taskId, normalized.PriorityName, ct);
                }

                if (!string.IsNullOrWhiteSpace(normalized.StatusName))
                {
                    await taskWrite.SetStatusByNameAsync(taskId, normalized.StatusName, ct);
                }
                else if (normalized.Done)
                {
                    // No explicit status mapped, but the source marked the item done/closed (e.g. a
                    // closed Trello card) — best-effort "complete" rather than silently dropping the signal.
                    await taskWrite.SetStatusByNameAsync(taskId, "Done", ct);
                }

                if (normalized.DueDate is { } due)
                {
                    await taskWrite.SetDueDateAsync(taskId, due, ct);
                }

                foreach (var tag in normalized.Tags)
                {
                    await taskWrite.AddTagByNameAsync(taskId, tag, ct);
                }

                if (!string.IsNullOrWhiteSpace(normalized.AssigneeIdentifier)
                    && assigneeIdByIdentifier.TryGetValue(normalized.AssigneeIdentifier, out var assigneeId))
                {
                    // Unresolved identifiers (no matching member email/display name) are left unassigned
                    // rather than failing the row -- same "best effort, don't block the import" treatment
                    // as an unmatched Status/Priority name above.
                    await taskWrite.AssignAsync(taskId, assigneeId, ct);
                }

                row.MarkCommitted(taskId);
                committedSoFar++;
                job.AdvanceProgress(committedSoFar, Now);
                Audit("work.import_job.row_committed", nameof(ImportJobRow), row.Id, new { job.Id, row.RowIndex, taskId });
                await SaveAsync(ct);
            }
            catch (Exception ex) when (ex is NotFoundException or ForbiddenException or ValidationAppException)
            {
                row.MarkInvalid(ex.Message);
                await SaveAsync(ct);
            }
        }

        var finalRows = await jobRows.ListByJobAsync(job.Id, ct);
        var stillFailing = finalRows.Count(r => r.Status == ImportRowStatus.Invalid);
        if (stillFailing == 0)
        {
            job.CompleteCommit(Now);
        }
        else
        {
            job.FailCommit(Now);
        }

        Audit("work.import_job.commit_finished", nameof(ImportJob), job.Id, new { job.Status, job.ProcessedRows, stillFailing });
        await SaveAsync(ct);
        return await ToDtoAsync(job, ct);
    }

    public async Task<ImportJobDto> GetAsync(Guid jobId, CancellationToken ct)
        => await ToDtoAsync(await LoadAsync(jobId, ct), ct);

    public async Task<IReadOnlyList<ImportJobDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);
        var list = await jobs.ListByWorkspaceAsync(workspaceId, ct);
        var result = new List<ImportJobDto>(list.Count);
        foreach (var job in list)
        {
            result.Add(await ToDtoAsync(job, ct));
        }

        return result;
    }

    public async Task<IReadOnlyList<ImportJobRowDto>> ListRowsAsync(Guid jobId, CancellationToken ct)
    {
        var job = await LoadAsync(jobId, ct);
        var rows = await jobRows.ListByJobAsync(job.Id, ct);
        return rows.OrderBy(r => r.RowIndex)
            .Select(r => new ImportJobRowDto(r.Id, r.RowIndex, r.Status.ToString(), r.ErrorMessage, r.CreatedTaskId))
            .ToList();
    }

    /// <summary>Finds an existing Space/List by (case-insensitive) name in the workspace, creating it via
    /// the normal authorized path only when missing — idempotent across retries/restarts, so resuming a
    /// commit never creates a duplicate Space/List for a name already resolved earlier in this (or a
    /// prior, interrupted) run.</summary>
    private async Task<Guid> ResolveTargetListAsync(
        ImportJob job, string? rowSpaceName, string? rowListName,
        Dictionary<string, Guid> spaceIdByName, Dictionary<(Guid, string), Guid> listIdByKey, CancellationToken ct)
    {
        var spaceName = rowSpaceName ?? job.TargetSpaceName;
        var listName = rowListName ?? job.TargetListName;

        if (job.TargetListId is { } fixedListId && rowListName is null)
        {
            return fixedListId;
        }

        if (string.IsNullOrWhiteSpace(spaceName))
        {
            throw new ValidationAppException("No target Space was resolved for this row (map a Space column or set a default target Space).");
        }

        if (string.IsNullOrWhiteSpace(listName))
        {
            listName = "Imported";
        }

        if (!spaceIdByName.TryGetValue(spaceName, out var spaceId))
        {
            var workspaceId = RequireWorkspace();
            var existingSpace = (await spaces.ListByWorkspaceAsync(workspaceId, ct))
                .FirstOrDefault(s => !s.IsDeleted && string.Equals(s.Name, spaceName, StringComparison.OrdinalIgnoreCase));
            if (existingSpace is not null)
            {
                spaceId = existingSpace.Id;
            }
            else
            {
                var created = await spaceService.CreateAsync(new CreateSpaceCommand(spaceName, null, null, null), ct);
                spaceId = created.Id;
            }

            spaceIdByName[spaceName] = spaceId;
        }

        var listKey = (spaceId, listName);
        if (!listIdByKey.TryGetValue(listKey, out var listId))
        {
            var existingList = (await lists.ListBySpaceAsync(spaceId, ct))
                .FirstOrDefault(l => !l.IsDeleted && string.Equals(l.Name, listName, StringComparison.OrdinalIgnoreCase));
            if (existingList is not null)
            {
                listId = existingList.Id;
            }
            else
            {
                var created = await listService.CreateAsync(new CreateListCommand(spaceId, null, listName, null, null), ct);
                listId = created.Id;
            }

            listIdByKey[listKey] = listId;
        }

        return listId;
    }

    /// <summary>Builds a case-insensitive email/display-name -> userId lookup for every active member of
    /// the workspace, once per commit rather than once per row. A source assignee identifier that matches
    /// neither is left unresolved by the caller -- the row still commits, just unassigned, the same
    /// best-effort treatment an unmatched Status/Priority name already gets.</summary>
    private async Task<IReadOnlyDictionary<string, Guid>> BuildAssigneeLookupAsync(Guid workspaceId, CancellationToken ct)
    {
        var lookup = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var memberId in await roster.ListActiveMemberUserIdsAsync(workspaceId, ct))
        {
            var user = await users.FindByIdAsync(memberId, ct);
            if (user is null)
            {
                continue;
            }

            lookup[user.Email] = user.UserId;
            lookup[user.DisplayName] = user.UserId;
        }

        return lookup;
    }

    private async Task<ImportJob> LoadAsync(Guid jobId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        WorkManagementAuthorizer.EnsureManageStructure((await AccessAsync(workspaceId, ct))?.Role);
        var job = await jobs.FindAsync(workspaceId, jobId, ct) ?? throw new NotFoundException("Import job not found.");
        return job;
    }

    private static Dictionary<string, string> DeserializeMapping(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

    private async Task<ImportJobDto> ToDtoAsync(ImportJob job, CancellationToken ct)
    {
        var rows = await jobRows.ListByJobAsync(job.Id, ct);
        var committed = rows.Count(r => r.Status == ImportRowStatus.Committed);
        return new ImportJobDto(
            job.Id, job.SourceType, job.FileName, job.Status.ToString(), job.DetectedColumns, job.ColumnMappingJson,
            job.TargetSpaceName, job.TargetListName, job.TotalRows, committed, job.ErrorCount, job.CreatedAtUtc);
    }
}
