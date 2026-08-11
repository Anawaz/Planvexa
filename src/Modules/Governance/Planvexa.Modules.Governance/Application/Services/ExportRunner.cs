namespace Planvexa.Modules.Governance.Application.Services;

using System.IO.Compression;
using System.Text;
using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Domain;
using Planvexa.SharedContracts.Governance;

/// <summary>
/// Runs a single governed export job to completion: transitions Pending → Running, fetches the dataset
/// rows via the <see cref="IExportDataSource"/> contract, formats them as CSV, and records the artifact
/// (Completed) or the error (Failed). Invoked by the host background worker under a bound workspace context,
/// so data-source reads and the store isolate correctly. Idempotency is provided by the caller claiming
/// only Pending jobs and this method persisting the state transition.
///
/// The "full" dataset is handled differently from the flat "audit"/"tasks" datasets: instead of one CSV
/// held inline on <see cref="ExportJob.Artifact"/>, every workspace entity type is written as its own
/// CSV file inside a zip archive (CSV chosen over JSON per-entity for consistency with the existing
/// flat-export format and because CsvWriter is already here — no new dependency), which is saved through
/// <see cref="IFileStorage"/> and referenced by its storage path on <see cref="ExportJob.Artifact"/>.
/// </summary>
public sealed class ExportRunner(
    IExportJobStore jobs,
    IExportDataSource dataSource,
    IClock clock,
    IUnitOfWork unitOfWork,
    IFileStorage fileStorage)
{
    public async Task RunAsync(ExportJob job, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        job.Start(now);
        try
        {
            if (job.Dataset == "full")
            {
                var (artifactPath, totalRows) = await BuildFullArchiveAsync(job, ct);
                job.Complete(artifactPath, totalRows, clock.UtcNow);
            }
            else
            {
                var rows = await dataSource.GetRowsAsync(job.WorkspaceId, job.Dataset, ct);
                var csv = CsvWriter.Write(rows.Header, rows.Rows);
                job.Complete(csv, rows.Rows.Count, clock.UtcNow);
            }
        }
        catch (Exception ex)
        {
            job.Fail(ex.Message, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<(string ArtifactPath, int TotalRows)> BuildFullArchiveAsync(ExportJob job, CancellationToken ct)
    {
        var tables = await dataSource.GetFullWorkspaceArchiveAsync(job.WorkspaceId, ct);

        var totalRows = 0;
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, rows) in tables)
            {
                totalRows += rows.Rows.Count;
                var entry = zip.CreateEntry($"{name}.csv", CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                var csvBytes = Encoding.UTF8.GetBytes(CsvWriter.Write(rows.Header, rows.Rows));
                await entryStream.WriteAsync(csvBytes, ct);
            }
        }

        buffer.Position = 0;
        var artifactPath = $"exports/{job.WorkspaceId}/{job.Id}.zip";
        await fileStorage.SaveAsync(artifactPath, buffer, ct);
        return (artifactPath, totalRows);
    }

    /// <summary>Fetches the next batch of pending jobs across workspaces for the worker to process.</summary>
    public Task<IReadOnlyList<ExportJob>> ClaimPendingAsync(int max, CancellationToken ct = default)
        => jobs.ListPendingAsync(max, ct);
}
