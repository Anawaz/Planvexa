namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Planvexa.Modules.Documents.Application;
using Planvexa.Modules.Documents.Domain;

internal sealed class DocumentStore(PlanvexaDbContext db) : IDocumentStore
{
    public void Add(Document document) => db.Set<Document>().Add(document);

    public void Remove(Document document) => db.Set<Document>().Remove(document);

    public Task<Document?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Document>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Document?> FindWithVersionsAsync(Guid id, CancellationToken ct = default)
        => db.Set<Document>().Include(d => d.Versions).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Document>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Document>()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.UpdatedAtUtc).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, Guid?>> ListParentMapByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Document>()
            .Where(x => x.WorkspaceId == workspaceId)
            .Select(x => new { x.Id, x.ParentDocumentId })
            .ToDictionaryAsync(x => x.Id, x => x.ParentDocumentId, ct);
}

internal sealed class DocumentTemplateStore(PlanvexaDbContext db) : IDocumentTemplateStore
{
    public void Add(DocumentTemplate template) => db.Set<DocumentTemplate>().Add(template);

    public Task<DocumentTemplate?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<DocumentTemplate>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<DocumentTemplate>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<DocumentTemplate>()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
}

internal sealed class DocumentCommentStore(PlanvexaDbContext db) : IDocumentCommentStore
{
    public void Add(DocumentComment comment) => db.Set<DocumentComment>().Add(comment);

    public async Task<IReadOnlyList<DocumentComment>> ListByDocumentAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
        => await db.Set<DocumentComment>()
            .Where(x => x.WorkspaceId == workspaceId && x.DocumentId == documentId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);
}

internal sealed class DocumentShareLinkStore(PlanvexaDbContext db, MaintenanceConnection maintenance) : IDocumentShareLinkStore
{
    public void Add(DocumentShareLink link) => db.Set<DocumentShareLink>().Add(link);

    public Task<DocumentShareLink?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<DocumentShareLink>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<DocumentShareLink?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => maintenance.LookupAsync(db, () =>
            db.Set<DocumentShareLink>().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct));

    public async Task<IReadOnlyList<DocumentShareLink>> ListForDocumentAsync(Guid documentId, CancellationToken ct = default)
        => await db.Set<DocumentShareLink>().Where(x => x.DocumentId == documentId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
}
