namespace Planvexa.Modules.Documents.Domain;

using Planvexa.BuildingBlocks.Domain;

/// <summary>
/// A reusable Lexical content snapshot a "create document from template" operation can copy
/// into a new document's <see cref="Document.Content"/>. Mirrors the WorkTemplate shape (opaque
/// content blob + name, no structural replay needed here since a document has no sub-structure) rather than
/// reusing WorkTemplate itself, since WorkTemplate is WorkManagement-owned and Documents must not read or
/// write another module's tables directly (AGENTS.md rule: modular monolith boundaries).
/// </summary>
public sealed class DocumentTemplate : Entity, IWorkspaceOwned
{
    private DocumentTemplate()
    {
    }

    private DocumentTemplate(Guid id, Guid workspaceId, string name, string contentJson, Guid createdByUserId, DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        Name = name;
        ContentJson = contentJson;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string ContentJson { get; private set; } = LexicalJson.EmptyDocument;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static DocumentTemplate Create(Guid id, Guid workspaceId, string name, string contentJson, Guid createdByUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(id, nameof(id));
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        return new DocumentTemplate(id, workspaceId, name.Trim(), string.IsNullOrEmpty(contentJson) ? LexicalJson.EmptyDocument : contentJson, createdByUserId, nowUtc);
    }
}
