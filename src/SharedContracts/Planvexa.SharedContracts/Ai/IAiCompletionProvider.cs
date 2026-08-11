namespace Planvexa.SharedContracts.Ai;

/// <summary>The kind of AI assistance requested (shapes how the provider composes its response).</summary>
public enum AiTaskKind
{
    Summarize = 0,
    GenerateSubtasks = 1,
    SuggestPriority = 2,

    /// <summary>Summarize a task's recent comments (Collaboration), same access rule as <see cref="Summarize"/>.</summary>
    SummarizeComments = 3,

    /// <summary>Summarize a Document (Documents module), gated by <c>Document.CanBeViewedBy</c>.</summary>
    SummarizeDocument = 4,

    /// <summary>Summarize a chat channel's recent messages, gated by <c>ChatChannel.CanBeAccessedBy</c>.</summary>
    SummarizeChat = 5,

    /// <summary>Flag a task as at-risk (overdue/blocked) with an optional generated explanation.</summary>
    RiskDetect = 6,

    /// <summary>Detect likely duplicate tasks within a List (deterministic similarity; never calls a real LLM).</summary>
    DetectDuplicate = 7,

    /// <summary>Re-rank the cross-module search fan-out's already permission-filtered results.</summary>
    SemanticSearch = 8,

    /// <summary>Retrieval-augmented answer built only from the cross-module search fan-out's own
    /// permission-filtered results for the requesting user (never any other content). Named "Qna" rather
    /// than the fuller "WorkspaceQuestionAnswering" so the enum's string form fits AiRequest.Kind's
    /// varchar(24) column.</summary>
    WorkspaceQna = 9,
}

/// <summary>
/// A provider-agnostic prompt: the task kind plus the already-assembled, permission-checked content the
/// AI may use. The Ai module builds this from tenant data; the provider must not fetch anything itself.
/// </summary>
public sealed record AiPrompt(AiTaskKind Kind, string Title, string? Description, IReadOnlyList<string> Context);

/// <summary>
/// The provider's response: the produced text, an estimated token cost (for usage metering), and —
/// populated only by a real (non-offline) provider that applied the redaction pass before the
/// outbound call — how many sensitive matches were redacted and which pattern types matched. The offline
/// <c>ExtractiveAi</c> provider never leaves the server, so it never redacts and always reports 0/empty.
/// </summary>
public sealed record AiCompletion(string Text, int TokensEstimated, int RedactedCount = 0, IReadOnlyList<string>? RedactedTypes = null);

/// <summary>
/// Provider-agnostic AI completion contract. The API host registers a deterministic extractive default
/// (no external calls, fully testable); a real LLM provider is a drop-in replacement. The Ai module
/// depends only on this interface (AGENTS.md rule 7), never on a concrete provider.
/// </summary>
public interface IAiCompletionProvider
{
    Task<AiCompletion> CompleteAsync(AiPrompt prompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Host-provided live connectivity probe for an OpenAI-compatible endpoint, used by the settings UI's
/// "Test connection" action. Takes the candidate settings explicitly so an admin can verify a
/// configuration before enabling it. Returns null when the call succeeded, otherwise an error message.
/// </summary>
public interface IAiProviderProbe
{
    Task<string?> TestAsync(string baseUrl, string model, string apiKey, CancellationToken cancellationToken = default);
}

/// <summary>Permission-checkable content for a task, assembled for AI prompts.</summary>
public sealed record AiTaskContent(
    Guid TaskId, Guid WorkspaceId, string Title, string? Description, bool IsCompleted, string Priority,
    DateTimeOffset? DueDate, IReadOnlyList<string> ChecklistItems, IReadOnlyList<string> RecentComments,
    bool IsBlocked = false);

/// <summary>
/// Contract (implemented in Infrastructure, which owns the DbContext) that returns a task's content for
/// AI prompts without the Ai module touching WorkManagement/Collaboration tables directly. Runs under the
/// ambient tenant; returns null when the task does not exist in the tenant.
/// </summary>
public interface IAiTaskContentSource
{
    Task<AiTaskContent?> GetAsync(Guid taskId, CancellationToken cancellationToken = default);
}

/// <summary>Permission-checkable content for a Document, assembled for AI prompts.</summary>
public sealed record AiDocumentContent(Guid DocumentId, Guid WorkspaceId, string Title, string PlainText);

/// <summary>
/// Contract (implemented in Infrastructure) that returns a Document's content for AI prompts, applying the
/// exact same <c>Document.CanBeViewedBy</c> check DocumentService/DocumentSearchProvider apply. Returns
/// null when the document does not exist or the requesting user cannot view it (private, not the owner) —
/// the Ai module must never see content it isn't allowed to read.
/// </summary>
public interface IAiDocumentContentSource
{
    Task<AiDocumentContent?> GetAsync(Guid documentId, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Permission-checkable content for a chat channel, assembled for AI prompts.</summary>
public sealed record AiChatContent(Guid ChannelId, Guid WorkspaceId, string ChannelName, IReadOnlyList<string> RecentMessages);

/// <summary>
/// Contract (implemented in Infrastructure) that returns a chat channel's recent messages for AI prompts,
/// applying the exact same <c>ChatChannel.CanBeAccessedBy</c> check ChatChannelService applies for reads.
/// <paramref name="isWorkspaceMember"/> is the caller's already-resolved workspace role (Member+); returns
/// null when the channel does not exist or the caller cannot access it.
/// </summary>
public interface IAiChatContentSource
{
    Task<AiChatContent?> GetAsync(Guid channelId, Guid userId, bool isWorkspaceMember, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cross-module read of the workspace's "allow AI to be completely disabled" master switch (see
/// <c>AiProviderSettings.AiFeaturesEnabled</c>), for modules other than Ai that gate their own AI-flavored
/// features (e.g. WorkManagement's deterministic duplicate-task detection) without taking a direct
/// dependency on the Ai module (AGENTS.md rule 7).
/// </summary>
public interface IAiFeatureGate
{
    /// <summary>True (the default) when the workspace has no AI-disable configured, or AI is explicitly enabled.</summary>
    Task<bool> IsEnabledAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
