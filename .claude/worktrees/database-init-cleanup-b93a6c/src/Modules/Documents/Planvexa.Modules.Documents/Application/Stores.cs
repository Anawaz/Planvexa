namespace Planvexa.Modules.Documents.Application;

using Planvexa.Modules.Documents.Domain;

public interface IDocumentStore
{
    void Add(Document document);
    void Remove(Document document);
    Task<Document?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Document?> FindWithVersionsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Every document id → parent id in the workspace, for O(1) cycle-prevention walks
    /// (<see cref="DocumentHierarchy.CreatesCycle"/>) without loading full entities.</summary>
    Task<IReadOnlyDictionary<Guid, Guid?>> ListParentMapByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IDocumentTemplateStore
{
    void Add(DocumentTemplate template);
    Task<DocumentTemplate?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentTemplate>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}
