namespace Planvexa.Modules.Ai.Domain;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.SharedContracts.Ai;

/// <summary>
/// An immutable log of an AI assistance request. Idempotent per (workspace, request key): a repeated request
/// with the same key returns the original result without re-invoking the provider (no double-charge).
/// Records the kind, target entity, a token-usage estimate, and the produced result text.
/// </summary>
public sealed class AiRequest : Entity, IWorkspaceOwned
{
    private AiRequest()
    {
    }

    private AiRequest(
        Guid id, Guid workspaceId, Guid userId, string requestKey, AiTaskKind kind,
        Guid entityId, int tokensEstimated, string result, DateTimeOffset nowUtc,
        int redactedCount, string redactedTypes)
        : base(id)
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        RequestKey = requestKey;
        Kind = kind;
        EntityId = entityId;
        TokensEstimated = tokensEstimated;
        Result = result;
        CreatedAtUtc = nowUtc;
        RedactedCount = redactedCount;
        RedactedTypes = redactedTypes;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>Idempotency key (client-supplied or derived from kind+entity+content).</summary>
    public string RequestKey { get; private set; } = string.Empty;

    public AiTaskKind Kind { get; private set; }
    public Guid EntityId { get; private set; }
    public int TokensEstimated { get; private set; }
    public string Result { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>item 3: how many sensitive matches the redaction pass stripped before this request's
    /// content (if any) was sent to a real AI provider. Always 0 for the offline extractive provider.</summary>
    public int RedactedCount { get; private set; }

    /// <summary>Comma-separated pattern types that matched (e.g. "email,api_key") — never the matched values themselves.</summary>
    public string RedactedTypes { get; private set; } = string.Empty;

    public static AiRequest Record(
        Guid id, Guid workspaceId, Guid userId, string requestKey, AiTaskKind kind,
        Guid entityId, int tokensEstimated, string result, DateTimeOffset nowUtc,
        int redactedCount = 0, string redactedTypes = "")
    {
        Guard.AgainstNullOrWhiteSpace(requestKey, nameof(requestKey));
        if (tokensEstimated < 0)
        {
            throw new ValidationAppException("Token estimate cannot be negative.");
        }

        if (redactedCount < 0)
        {
            throw new ValidationAppException("Redacted count cannot be negative.");
        }

        return new AiRequest(id, workspaceId, userId, requestKey, kind, entityId, tokensEstimated, result, nowUtc, redactedCount, redactedTypes ?? string.Empty);
    }
}
