namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Ai.Application;
using Planvexa.Modules.Ai.Domain;
using Planvexa.Modules.Chat.Domain;
using Planvexa.Modules.Collaboration.Domain;
using Planvexa.Modules.Documents.Domain;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Ai;

internal sealed class AiRequestStore(PlanvexaDbContext db) : IAiRequestStore
{
    public void Add(AiRequest request) => db.Set<AiRequest>().Add(request);

    public Task<AiRequest?> FindByKeyAsync(Guid workspaceId, string requestKey, CancellationToken ct = default)
        => db.Set<AiRequest>().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.RequestKey == requestKey, ct);

    public Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => db.Set<AiRequest>().CountAsync(x => x.WorkspaceId == workspaceId, ct);

    public async Task<long> SumTokensForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<AiRequest>().Where(x => x.WorkspaceId == workspaceId).SumAsync(x => (long?)x.TokensEstimated, ct) ?? 0L;

    public async Task<long> SumTokensForWorkspaceSinceAsync(Guid workspaceId, DateTimeOffset sinceUtc, CancellationToken ct = default)
        => await db.Set<AiRequest>()
            .Where(x => x.WorkspaceId == workspaceId && x.CreatedAtUtc >= sinceUtc)
            .SumAsync(x => (long?)x.TokensEstimated, ct) ?? 0L;
}

internal sealed class AiProviderSettingsStore(PlanvexaDbContext db) : IAiProviderSettingsStore
{
    public void Add(AiProviderSettings settings) => db.Set<AiProviderSettings>().Add(settings);

    public Task<AiProviderSettings?> FindAsync(Guid workspaceId, CancellationToken ct = default)
        => db.Set<AiProviderSettings>().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
}

/// <summary>See <see cref="SharedContracts.Ai.IAiFeatureGate"/>. No row for the workspace means AI was
/// never disabled — the default is enabled.</summary>
internal sealed class AiFeatureGate(PlanvexaDbContext db) : SharedContracts.Ai.IAiFeatureGate
{
    public async Task<bool> IsEnabledAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        => (await db.Set<AiProviderSettings>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken))?.AiFeaturesEnabled ?? true;
}

/// <summary>
/// Implements the cross-module <see cref="IAiTaskContentSource"/> by assembling a task's content from
/// WorkManagement (title, description, checklist items) and Collaboration (recent comments), so the AI
/// module never touches those tables directly. Runs under the ambient workspace query filter.
/// </summary>
internal sealed class AiTaskContentSource(PlanvexaDbContext db) : IAiTaskContentSource
{
    public async Task<AiTaskContent?> GetAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await db.Set<WorkItem>()
            .FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var checklistItems = await (
            from item in db.Set<TaskChecklistItem>()
            join cl in db.Set<TaskChecklist>() on item.ChecklistId equals cl.Id
            where cl.TaskId == taskId
            orderby item.Position
            select item.Content)
            .Take(50)
            .ToListAsync(cancellationToken);

        var comments = await db.Set<Comment>()
            .Where(c => c.TaskId == taskId && !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(5)
            .Select(c => c.Body)
            .ToListAsync(cancellationToken);

        // Risk detection: "blocked" means an unfinished dependency this task is BlockedBy/WaitingOn.
        var isBlocked = await (
            from dep in db.Set<TaskDependency>()
            join blocker in db.Set<WorkItem>() on dep.DependsOnTaskId equals blocker.Id
            where dep.TaskId == taskId
                && (dep.Type == DependencyType.BlockedBy || dep.Type == DependencyType.WaitingOn)
                && !blocker.IsCompleted && !blocker.IsDeleted
            select dep.Id)
            .AnyAsync(cancellationToken);

        return new AiTaskContent(
            task.Id, task.WorkspaceId, task.Title, task.Description, task.IsCompleted, task.Priority.ToString(),
            task.DueDate, checklistItems, comments, isBlocked);
    }
}

/// <summary>
/// Implements the cross-module <see cref="IAiDocumentContentSource"/> for Document summaries,
/// applying the exact same <see cref="Document.CanBeViewedBy"/> private-owner check DocumentService and
/// DocumentSearchProvider apply — a private document belonging to someone else must never reach the Ai
/// module's prompt builder.
/// </summary>
internal sealed class AiDocumentContentSource(PlanvexaDbContext db) : IAiDocumentContentSource
{
    public async Task<AiDocumentContent?> GetAsync(Guid documentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var document = await db.Set<Document>().FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null || !document.CanBeViewedBy(userId))
        {
            return null;
        }

        return new AiDocumentContent(document.Id, document.WorkspaceId, document.Title, LexicalJson.ExtractPlainText(document.Content));
    }
}

/// <summary>
/// Implements the cross-module <see cref="IAiChatContentSource"/> for chat-channel summaries,
/// applying the exact same <see cref="ChatChannel.CanBeAccessedBy"/> check ChatChannelService applies to
/// reads — a private/DM channel the caller is not a member of must never reach the Ai module's prompt
/// builder, and a Workspace-type channel requires at least workspace membership.
/// </summary>
internal sealed class AiChatContentSource(PlanvexaDbContext db) : IAiChatContentSource
{
    public async Task<AiChatContent?> GetAsync(Guid channelId, Guid userId, bool isWorkspaceMember, CancellationToken cancellationToken = default)
    {
        var channel = await db.Set<ChatChannel>().FirstOrDefaultAsync(c => c.Id == channelId, cancellationToken);
        if (channel is null || !channel.CanBeAccessedBy(userId, isWorkspaceMember))
        {
            return null;
        }

        var messages = await db.Set<ChatMessage>()
            .Where(m => m.ChannelId == channelId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(30)
            .Select(m => m.Body)
            .ToListAsync(cancellationToken);
        messages.Reverse();

        return new AiChatContent(channel.Id, channel.WorkspaceId, channel.Name, messages);
    }
}
