namespace Planvexa.Modules.WorkManagement.Application.Services;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Primitives;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Ai;

/// <summary>A candidate duplicate: another task in the same List with a similarity score in [0, 1].</summary>
public sealed record DuplicateCandidateDto(Guid TaskId, string Title, double Score);

/// <summary>
/// Duplicate-task detection. Deterministic (Jaccard title/description token overlap via the
/// shared-kernel <see cref="TextSimilarity"/>) — this never calls an LLM, so it works identically online
/// or offline, and needs no AI provider at all.
///
/// SECURITY: candidates are drawn from the task's own List (same scope a person browsing that List would
/// see) and every candidate is re-checked with <see cref="WorkServiceBase.CanReadAsync"/> before it can be
/// returned — the same per-resource permission check Search/Document/Chat apply, so a private task the
/// caller cannot read is never surfaced as a "possible duplicate", never mind cross-workspace.
/// </summary>
public sealed class DuplicateTaskService(WorkServiceContext ctx, IWorkItemStore tasks, IAiFeatureGate aiFeatureGate)
    : WorkServiceBase(ctx)
{
    /// <summary>Below this Jaccard score, two tasks are not considered plausible duplicates.</summary>
    public const double Threshold = 0.35;

    private const int MaxCandidates = 5;

    public async Task<IReadOnlyList<DuplicateCandidateDto>> FindDuplicatesAsync(Guid taskId, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        if (!await aiFeatureGate.IsEnabledAsync(workspaceId, ct))
        {
            throw new ForbiddenException("AI has been disabled for this workspace.");
        }

        var task = await tasks.FindAsync(taskId, ct) ?? throw new NotFoundException("Task not found.");
        if (task.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Task not found in this workspace.");
        }

        if (!await CanReadAsync(task, WorkResourceTypes.Task, ct))
        {
            throw new NotFoundException("Task not found.");
        }

        var siblings = await tasks.ListByListAsync(task.ListId, ct);
        var scored = new List<DuplicateCandidateDto>();
        foreach (var candidate in siblings)
        {
            if (candidate.Id == task.Id || candidate.IsDeleted)
            {
                continue;
            }

            var score = TextSimilarity.Jaccard($"{task.Title} {task.Description}", $"{candidate.Title} {candidate.Description}");
            if (score < Threshold)
            {
                continue;
            }

            // Never surface a candidate the requester could not themselves read.
            if (!await CanReadAsync(candidate, WorkResourceTypes.Task, ct))
            {
                continue;
            }

            scored.Add(new DuplicateCandidateDto(candidate.Id, candidate.Title, Math.Round(score, 2)));
        }

        return scored.OrderByDescending(s => s.Score).Take(MaxCandidates).ToList();
    }
}
