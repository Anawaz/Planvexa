namespace Planvexa.Modules.Governance.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// A governed data export request and its generated artifact. For "audit"/"tasks" datasets,
/// <see cref="Artifact"/> holds the CSV content inline. For "full" (a zip archive of every workspace
/// entity type), <see cref="Artifact"/> instead holds the file-storage path where the zip bytes were saved.
/// </summary>
public sealed class ExportJob : Entity, IWorkspaceOwned
{
    private static readonly HashSet<string> AllowedDatasets = new(StringComparer.Ordinal)
    {
        "audit",
        "tasks",
        "full",
    };

    private ExportJob()
    {
    }

    private ExportJob(
        Guid id,
        Guid workspaceId,
        string dataset,
        Guid requestedByUserId,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Dataset = dataset;
        RequestedByUserId = requestedByUserId;
        CreatedAtUtc = createdAtUtc;
        Status = ExportJobStatus.Pending;
    }

    public Guid WorkspaceId { get; private set; }
    public string Dataset { get; private set; } = string.Empty;
    public Guid RequestedByUserId { get; private set; }
    public ExportJobStatus Status { get; private set; }
    public string? Artifact { get; private set; }
    public int? RowCount { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static ExportJob Create(Guid id, Guid workspaceId, string dataset, Guid requestedBy, DateTimeOffset nowUtc)
    {
        var normalizedDataset = NormalizeDataset(dataset);
        return new ExportJob(id, workspaceId, normalizedDataset, requestedBy, nowUtc);
    }

    public void Start(DateTimeOffset nowUtc)
    {
        if (Status != ExportJobStatus.Pending)
        {
            throw new ValidationAppException("Only pending exports can be started.");
        }

        Status = ExportJobStatus.Running;
        Error = null;
        CompletedAtUtc = null;
    }

    public void Complete(string artifact, int rowCount, DateTimeOffset nowUtc)
    {
        if (Status != ExportJobStatus.Running)
        {
            throw new ValidationAppException("Only running exports can be completed.");
        }

        if (rowCount < 0)
        {
            throw new ValidationAppException("Export row count cannot be negative.");
        }

        Artifact = artifact;
        RowCount = rowCount;
        Error = null;
        Status = ExportJobStatus.Completed;
        CompletedAtUtc = nowUtc;
    }

    public void Fail(string error, DateTimeOffset nowUtc)
    {
        Error = string.IsNullOrWhiteSpace(error) ? "Export failed." : error.Trim();
        Status = ExportJobStatus.Failed;
        CompletedAtUtc = nowUtc;
    }

    private static string NormalizeDataset(string dataset)
    {
        var normalized = dataset.Trim().ToLowerInvariant();
        if (!AllowedDatasets.Contains(normalized))
        {
            throw new ValidationAppException("Export dataset must be one of 'audit', 'tasks', or 'full'.");
        }

        return normalized;
    }
}

