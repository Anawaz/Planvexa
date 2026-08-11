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

public interface IDocumentCommentStore
{
    void Add(DocumentComment comment);
    Task<IReadOnlyList<DocumentComment>> ListByDocumentAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default);
}

public interface IDocumentShareLinkStore
{
    void Add(DocumentShareLink link);
    Task<DocumentShareLink?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Cross-workspace lookup by token hash (anonymous read path — the token is the credential).</summary>
    Task<DocumentShareLink?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task<IReadOnlyList<DocumentShareLink>> ListForDocumentAsync(Guid documentId, CancellationToken ct = default);
}
