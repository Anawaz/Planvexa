namespace Planvexa.Modules.Governance.Application.Services;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Governance.Application;
using Planvexa.Modules.Governance.Authorization;
using Planvexa.Modules.Governance.Domain;

/// <summary>The bytes to send back for a completed export: inline CSV text for "audit"/"tasks", or a
/// streamed zip file for "full" (opened from <see cref="IFileStorage"/>, disposed by the caller).</summary>
public sealed record ExportDownload(string ContentType, string FileName, Stream Content);

/// <summary>Creates and reads governed export jobs for a workspace.</summary>
public sealed class ExportJobService(
    GovernanceServiceContext ctx,
    IExportJobStore store,
    IFileStorage fileStorage)
    : GovernanceServiceBase(ctx)
{
    public async Task<IReadOnlyList<ExportJobDto>> ListAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var jobs = await store.ListByWorkspaceAsync(workspaceId, ct);
        return jobs.Select(ToDto).ToList();
    }

    public async Task<ExportJobDto> CreateAsync(string dataset, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var job = ExportJob.Create(NewId(), workspaceId, dataset, UserId, Now);
        store.Add(job);
        Audit("governance.export.created", "ExportJob", job.Id, new { dataset });
        await SaveAsync(ct);
        return ToDto(job);
    }

    public async Task<ExportJobDto> GetAsync(Guid id, CancellationToken ct)
    {
        var job = await FindForWorkspaceAsync(id, ct);
        return ToDto(job);
    }

    public async Task<ExportDownload> DownloadAsync(Guid id, CancellationToken ct)
    {
        var job = await FindForWorkspaceAsync(id, ct);
        if (job.Status != ExportJobStatus.Completed)
        {
            throw new ValidationAppException("Export is not ready.");
        }

        if (job.Dataset == "full")
        {
            var stream = await fileStorage.OpenReadAsync(job.Artifact ?? string.Empty, ct);
            return new ExportDownload("application/zip", $"export-{id}.zip", stream);
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(job.Artifact ?? string.Empty);
        return new ExportDownload("text/csv", $"export-{id}.csv", new MemoryStream(bytes));
    }

    private async Task<ExportJob> FindForWorkspaceAsync(Guid id, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        GovernanceAuthorizer.EnsureManage((await AccessAsync(workspaceId, ct))?.Role);

        var job = await store.FindAsync(id, ct);
        if (job is null || job.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Export job not found.");
        }

        return job;
    }

    private static ExportJobDto ToDto(ExportJob job)
        => new(job.Id, job.Dataset, job.Status.ToString(), job.CreatedAtUtc, job.CompletedAtUtc, job.RowCount);
}

