namespace Planvexa.Modules.Ai.Application.Services;

using System.Security.Cryptography;
using System.Text;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.Ai.Authorization;
using Planvexa.Modules.Ai.Domain;
using Planvexa.SharedContracts.Ai;
using Planvexa.SharedContracts.Workspaces;

/// <summary>
/// Permission-aware AI assistance over tasks. Every operation: verifies workspace
/// access (Member+), loads the task's content through
/// the <see cref="IAiTaskContentSource"/> contract (never touching WorkManagement tables directly),
/// invokes the provider-agnostic <see cref="IAiCompletionProvider"/>, and logs an idempotent
/// <see cref="AiRequest"/> so retries never re-invoke the provider or double-charge.
/// </summary>
public sealed class AiAssistService(
    AiServiceContext ctx,
    IAiRequestStore requests,
    IAiTaskContentSource content,
    IAiDocumentContentSource documentContent,
    IAiChatContentSource chatContent,
    IAiCompletionProvider provider)
    : AiServiceBase(ctx)
{
    public async Task<AiSummaryDto> SummarizeAsync(Guid taskId, string? idempotencyKey, CancellationToken ct)
    {
        var (result, taskContent) = await RunAsync(AiTaskKind.Summarize, taskId, idempotencyKey, ct);
        return new AiSummaryDto(taskContent.TaskId, result.Result, result.TokensEstimated);
    }

    public async Task<AiSubtasksDto> SuggestSubtasksAsync(Guid taskId, string? idempotencyKey, CancellationToken ct)
    {
        var (result, _) = await RunAsync(AiTaskKind.GenerateSubtasks, taskId, idempotencyKey, ct);
        var titles = result.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new AiSubtasksDto(titles, result.TokensEstimated);
    }

    public async Task<AiPriorityDto> SuggestPriorityAsync(Guid taskId, string? idempotencyKey, CancellationToken ct)
    {
        var (result, _) = await RunAsync(AiTaskKind.SuggestPriority, taskId, idempotencyKey, ct);
        var parts = result.Result.Split('|', 2);
        var priority = parts.Length > 0 ? parts[0] : "Normal";
        var rationale = parts.Length > 1 ? parts[1] : string.Empty;
        return new AiPriorityDto(priority, rationale, result.TokensEstimated);
    }

    /// <summary>Summarizes a task's recent comments (Collaboration). Same access rule as <see cref="SummarizeAsync"/>:
    /// workspace Member+ and the task must exist in this workspace — the comments were already fetched
    /// through <see cref="IAiTaskContentSource"/>, so this never touches Collaboration tables directly.</summary>
    public async Task<AiSummaryDto> SummarizeCommentsAsync(Guid taskId, string? idempotencyKey, CancellationToken ct)
    {
        var (result, taskContent) = await RunAsync(AiTaskKind.SummarizeComments, taskId, idempotencyKey, ct);
        return new AiSummaryDto(taskContent.TaskId, result.Result, result.TokensEstimated);
    }

    /// <summary>Deterministic-first risk flag for a task (overdue / blocked / due soon), with an optional
    /// provider-generated explanation. Same access rule as <see cref="SummarizeAsync"/>.</summary>
    public async Task<AiRiskDto> DetectRiskAsync(Guid taskId, string? idempotencyKey, CancellationToken ct)
    {
        var (result, _) = await RunAsync(AiTaskKind.RiskDetect, taskId, idempotencyKey, ct);
        var parts = result.Result.Split('|', 2);
        var status = parts.Length > 0 ? parts[0] : "OnTrack";
        var reason = parts.Length > 1 ? parts[1] : string.Empty;
        return new AiRiskDto(status == "AtRisk", status, reason, result.TokensEstimated);
    }

    /// <summary>Summarizes a Document. Permission-checked via <see cref="AiDocumentContent"/>'s source,
    /// which applies the exact same <c>Document.CanBeViewedBy</c> check DocumentService applies to reads.</summary>
    public async Task<AiSummaryDto> SummarizeDocumentAsync(Guid documentId, string? idempotencyKey, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AiAuthorizer.EnsureUse((await AccessAsync(workspaceId, ct))?.Role);

        var doc = await documentContent.GetAsync(documentId, UserId, ct)
            ?? throw new NotFoundException("Document not found.");
        if (doc.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Document not found in this workspace.");
        }

        var prompt = new AiPrompt(AiTaskKind.SummarizeDocument, doc.Title, doc.PlainText, []);
        var requestKey = idempotencyKey?.Trim() is { Length: > 0 } clientKey
            ? clientKey
            : DeriveDocumentKey(doc);

        var existing = await requests.FindByKeyAsync(workspaceId, requestKey, ct);
        if (existing is not null)
        {
            return new AiSummaryDto(doc.DocumentId, existing.Result, existing.TokensEstimated);
        }

        var completion = await provider.CompleteAsync(prompt, ct);
        var request = AiRequest.Record(
            NewId(), workspaceId, UserId, requestKey, AiTaskKind.SummarizeDocument, documentId,
            completion.TokensEstimated, completion.Text, Now, completion.RedactedCount, string.Join(',', completion.RedactedTypes ?? []));
        requests.Add(request);
        Audit("ai.summarizedocument", "AiRequest", request.Id, new { documentId, request.TokensEstimated });
        await SaveAsync(ct);
        return new AiSummaryDto(doc.DocumentId, request.Result, request.TokensEstimated);
    }

    /// <summary>Summarizes a chat channel's recent messages. Permission-checked via <see cref="AiChatContent"/>'s
    /// source, which applies the exact same <c>ChatChannel.CanBeAccessedBy</c> check ChatChannelService
    /// applies to reads.</summary>
    public async Task<AiSummaryDto> SummarizeChatAsync(Guid channelId, string? idempotencyKey, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        var role = (await AccessAsync(workspaceId, ct))?.Role;
        AiAuthorizer.EnsureUse(role);

        var channel = await chatContent.GetAsync(channelId, UserId, role >= WorkspaceRole.Member, ct)
            ?? throw new NotFoundException("Chat channel not found.");
        if (channel.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Chat channel not found in this workspace.");
        }

        var prompt = new AiPrompt(AiTaskKind.SummarizeChat, channel.ChannelName, null, channel.RecentMessages);
        var requestKey = idempotencyKey?.Trim() is { Length: > 0 } clientKey
            ? clientKey
            : DeriveChatKey(channel);

        var existing = await requests.FindByKeyAsync(workspaceId, requestKey, ct);
        if (existing is not null)
        {
            return new AiSummaryDto(channel.ChannelId, existing.Result, existing.TokensEstimated);
        }

        var completion = await provider.CompleteAsync(prompt, ct);
        var request = AiRequest.Record(
            NewId(), workspaceId, UserId, requestKey, AiTaskKind.SummarizeChat, channelId,
            completion.TokensEstimated, completion.Text, Now, completion.RedactedCount, string.Join(',', completion.RedactedTypes ?? []));
        requests.Add(request);
        Audit("ai.summarizechat", "AiRequest", request.Id, new { channelId, request.TokensEstimated });
        await SaveAsync(ct);
        return new AiSummaryDto(channel.ChannelId, request.Result, request.TokensEstimated);
    }

    public async Task<AiUsageDto> GetUsageAsync(CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AiAuthorizer.EnsureRead((await AccessAsync(workspaceId, ct))?.Role);

        var count = await requests.CountForWorkspaceAsync(workspaceId, ct);
        var tokens = await requests.SumTokensForWorkspaceAsync(workspaceId, ct);
        return new AiUsageDto(count, tokens, true, null);
    }

    private async Task<(AiRequest Result, AiTaskContent Content)> RunAsync(AiTaskKind kind, Guid taskId, string? idempotencyKey, CancellationToken ct)
    {
        var workspaceId = RequireWorkspace();
        AiAuthorizer.EnsureUse((await AccessAsync(workspaceId, ct))?.Role);

        // Permission-aware content: the task must exist in this workspace.
        var taskContent = await content.GetAsync(taskId, ct)
            ?? throw new NotFoundException("Task not found.");
        if (taskContent.WorkspaceId != workspaceId)
        {
            throw new NotFoundException("Task not found in this workspace.");
        }

        var prompt = BuildPrompt(kind, taskContent);
        var requestKey = idempotencyKey?.Trim() is { Length: > 0 } clientKey
            ? clientKey
            : DeriveKey(kind, taskContent);

        // Idempotency: replay a prior identical request without re-invoking the provider.
        var existing = await requests.FindByKeyAsync(workspaceId, requestKey, ct);
        if (existing is not null)
        {
            return (existing, taskContent);
        }

        var completion = await provider.CompleteAsync(prompt, ct);
        var request = AiRequest.Record(
            NewId(), workspaceId, UserId, requestKey, kind, taskId, completion.TokensEstimated, completion.Text, Now,
            completion.RedactedCount, string.Join(',', completion.RedactedTypes ?? []));
        requests.Add(request);
        Audit($"ai.{kind.ToString().ToLowerInvariant()}", "AiRequest", request.Id, new { taskId, request.TokensEstimated });
        await SaveAsync(ct);
        return (request, taskContent);
    }

    private AiPrompt BuildPrompt(AiTaskKind kind, AiTaskContent c)
    {
        var context = new List<string>();
        switch (kind)
        {
            case AiTaskKind.Summarize:
                context.AddRange(c.ChecklistItems);
                context.AddRange(c.RecentComments);
                break;
            case AiTaskKind.GenerateSubtasks:
                context.AddRange(c.ChecklistItems);
                break;
            case AiTaskKind.SuggestPriority:
                if (!c.IsCompleted && c.DueDate is { } due && due < Now)
                {
                    context.Add("overdue");
                }

                context.Add($"priority: {c.Priority}");
                break;
            case AiTaskKind.SummarizeComments:
                context.AddRange(c.RecentComments);
                break;
            case AiTaskKind.RiskDetect:
                if (!c.IsCompleted && c.DueDate is { } dueDate)
                {
                    if (dueDate < Now)
                    {
                        context.Add("overdue");
                    }
                    else if (dueDate <= Now.AddDays(2))
                    {
                        context.Add("due-soon");
                    }
                }

                if (c.IsBlocked)
                {
                    context.Add("blocked");
                }

                break;
        }

        return new AiPrompt(kind, c.Title, c.Description, context);
    }

    private static string DeriveKey(AiTaskKind kind, AiTaskContent c)
    {
        // Deterministic key from the task's current content, so an unchanged task replays and an edited
        // task produces a fresh request.
        var material = string.Join('\u001f',
            c.Title, c.Description ?? string.Empty, c.Priority, c.IsCompleted, c.DueDate?.ToString("O") ?? string.Empty,
            string.Join('\u001e', c.ChecklistItems), string.Join('\u001e', c.RecentComments));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..16];
        return $"{kind}:{c.TaskId}:{hash}";
    }

    private static string DeriveDocumentKey(AiDocumentContent c)
    {
        var material = string.Join("::", c.Title, c.PlainText);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..16];
        return $"{AiTaskKind.SummarizeDocument}:{c.DocumentId}:{hash}";
    }

    private static string DeriveChatKey(AiChatContent c)
    {
        var material = string.Join("::", c.ChannelName, string.Join("::", c.RecentMessages));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..16];
        return $"{AiTaskKind.SummarizeChat}:{c.ChannelId}:{hash}";
    }
}
