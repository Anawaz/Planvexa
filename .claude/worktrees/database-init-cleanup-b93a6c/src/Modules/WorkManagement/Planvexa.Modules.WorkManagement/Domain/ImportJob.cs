namespace Planvexa.Modules.WorkManagement.Domain;

using Planvexa.BuildingBlocks.Domain;

public enum ImportJobStatus
{
    Uploaded = 0,
    Validated = 1,
    Committing = 2,
    Completed = 3,
    Failed = 4,
}

public enum ImportRowStatus
{
    Pending = 0,
    Valid = 1,
    Invalid = 2,
    Committed = 3,
}

/// <summary>
/// A bulk data import: a source file/connection is parsed by an IImportSource into one
/// ImportJobRow per source record, validated, then committed - each row creates a real Space/List/Task
/// via the same authorized paths manual creation uses (see ImportJobService's doc comment). Resumable:
/// ProcessedRows / row-level ImportRowStatus.Committed state means a commit interrupted partway (e.g.
/// app restart) can be re-invoked and will skip already-committed rows rather than restart or duplicate
/// (AGENTS.md rule 13).
/// </summary>
public sealed class ImportJob : Entity, IWorkspaceOwned
{
    /// <summary>Column separator for <see cref="DetectedColumnsCsv"/>: unit separator (0x1F), not a comma
    /// - a header cell can legitimately contain a comma, but never a control character.</summary>
    public static readonly char ColumnSeparator = (char)0x1F;

    private ImportJob()
    {
    }

    private ImportJob(
        Guid id, Guid workspaceId, string sourceType, string fileName, string? targetSpaceName,
        string? targetListName, Guid? targetSpaceId, Guid? targetListId, Guid createdByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        SourceType = sourceType;
        FileName = fileName;
        Status = ImportJobStatus.Uploaded;
        TargetSpaceName = targetSpaceName;
        TargetListName = targetListName;
        TargetSpaceId = targetSpaceId;
        TargetListId = targetListId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public ImportJobStatus Status { get; private set; }
    public string? ColumnMappingJson { get; private set; }
    public string? DetectedColumnsCsv { get; private set; }

    public IReadOnlyList<string> DetectedColumns =>
        (DetectedColumnsCsv ?? string.Empty).Split(ColumnSeparator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Fallback target for rows that don't carry their own space/list name (CSV/Excel, typically
    /// a single flat sheet). Trello's structured source sets a per-row space/list name instead.</summary>
    public string? TargetSpaceName { get; private set; }
    public string? TargetListName { get; private set; }
    public Guid? TargetSpaceId { get; private set; }
    public Guid? TargetListId { get; private set; }

    public int TotalRows { get; private set; }
    public int ProcessedRows { get; private set; }
    public int ErrorCount { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ImportJob Create(
        Guid id, Guid workspaceId, string sourceType, string fileName, string? targetSpaceName,
        string? targetListName, Guid? targetSpaceId, Guid? targetListId, Guid createdByUserId, DateTimeOffset nowUtc)
        => new(id, workspaceId, sourceType, fileName, targetSpaceName, targetListName, targetSpaceId, targetListId, createdByUserId, nowUtc);

    public void SetTotalRows(int total, DateTimeOffset nowUtc)
    {
        TotalRows = total;
        UpdatedAtUtc = nowUtc;
    }

    public void SetDetectedColumns(IReadOnlyList<string> columns, DateTimeOffset nowUtc)
    {
        DetectedColumnsCsv = string.Join(ColumnSeparator, columns);
        UpdatedAtUtc = nowUtc;
    }

    public void SetColumnMapping(string? columnMappingJson, DateTimeOffset nowUtc)
    {
        ColumnMappingJson = columnMappingJson;
        UpdatedAtUtc = nowUtc;
    }

    public void RecordValidation(int errorCount, DateTimeOffset nowUtc)
    {
        Status = ImportJobStatus.Validated;
        ErrorCount = errorCount;
        UpdatedAtUtc = nowUtc;
    }

    public void BeginCommit(DateTimeOffset nowUtc)
    {
        Status = ImportJobStatus.Committing;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Advances processed-row progress - called after each row commits so an interrupted commit
    /// resumes from an accurate count (AGENTS.md rule 13).</summary>
    public void AdvanceProgress(int processedRows, DateTimeOffset nowUtc)
    {
        ProcessedRows = processedRows;
        UpdatedAtUtc = nowUtc;
    }

    public void CompleteCommit(DateTimeOffset nowUtc)
    {
        Status = ImportJobStatus.Completed;
        UpdatedAtUtc = nowUtc;
    }

    public void FailCommit(DateTimeOffset nowUtc)
    {
        Status = ImportJobStatus.Failed;
        UpdatedAtUtc = nowUtc;
    }
}

/// <summary>One normalized source record. RawFieldsJson is a flat string-to-string map - for CSV/Excel
/// the raw column-header-to-cell values; for a structured source (Trello) the already-semantic keys the
/// parser emits directly (see TrelloImportSource). IdempotencyKey is deterministic (job id + row index),
/// so re-processing a row that already committed - the resumability case - is a safe, detectable no-op
/// rather than a duplicate task (AGENTS.md rule 13).</summary>
public sealed class ImportJobRow : Entity, IWorkspaceOwned
{
    private ImportJobRow()
    {
    }

    private ImportJobRow(Guid id, Guid workspaceId, Guid importJobId, int rowIndex, string idempotencyKey, string rawFieldsJson)
        : base(id)
    {
        WorkspaceId = workspaceId;
        ImportJobId = importJobId;
        RowIndex = rowIndex;
        IdempotencyKey = idempotencyKey;
        RawFieldsJson = rawFieldsJson;
        Status = ImportRowStatus.Pending;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid ImportJobId { get; private set; }
    public int RowIndex { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RawFieldsJson { get; private set; } = string.Empty;
    public ImportRowStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public Guid? CreatedTaskId { get; private set; }

    public static ImportJobRow Create(Guid id, Guid workspaceId, Guid importJobId, int rowIndex, string rawFieldsJson)
        => new(id, workspaceId, importJobId, rowIndex, $"{importJobId:N}:{rowIndex}", rawFieldsJson);

    public void MarkValid() => (Status, ErrorMessage) = (ImportRowStatus.Valid, null);

    public void MarkInvalid(string errorMessage) => (Status, ErrorMessage) = (ImportRowStatus.Invalid, errorMessage);

    public void MarkCommitted(Guid taskId) => (Status, ErrorMessage, CreatedTaskId) = (ImportRowStatus.Committed, null, taskId);
}
