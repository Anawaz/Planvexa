namespace Planvexa.Modules.Documents.Domain;

using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Exceptions;

/// <summary>A workspace document with private-owner visibility and immutable content snapshots.</summary>
public sealed class Document : Entity, IAggregateRoot, IWorkspaceOwned
{
    private readonly List<DocumentVersion> _versions = new();

    private Document()
    {
    }

    private Document(
        Guid id,
        Guid workspaceId,
        Guid ownerUserId,
        string title,
        string content,
        bool isPrivate,
        Guid? spaceId,
        Guid? listId,
        Guid? taskId,
        Guid? parentDocumentId,
        DateTimeOffset nowUtc)
        : base(id)
    {
        WorkspaceId = workspaceId;
        OwnerUserId = ownerUserId;
        Title = title;
        Content = content;
        IsPrivate = isPrivate;
        SpaceId = spaceId;
        ListId = listId;
        TaskId = taskId;
        ParentDocumentId = parentDocumentId;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public Guid WorkspaceId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public bool IsPrivate { get; private set; }
    public Guid? SpaceId { get; private set; }
    public Guid? ListId { get; private set; }
    public Guid? TaskId { get; private set; }

    /// <summary>Optional parent document — documents may nest into a wiki tree, independent of the
    /// Space/List/Task association above. Null = top-level document. Cycle prevention lives in
    /// <see cref="DocumentHierarchy"/>, mirroring the FolderHierarchy exactly.</summary>
    public Guid? ParentDocumentId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<DocumentVersion> Versions => _versions.AsReadOnly();

    public static Document Create(
        Guid id,
        Guid workspaceId,
        Guid ownerUserId,
        string title,
        string content,
        bool isPrivate,
        Guid? spaceId,
        Guid? listId,
        Guid? taskId,
        DateTimeOffset nowUtc,
        Guid? parentDocumentId = null)
    {
        Guard.AgainstNullOrWhiteSpace(title, nameof(title));
        Guard.AgainstEmpty(workspaceId, nameof(workspaceId));
        Guard.AgainstEmpty(ownerUserId, nameof(ownerUserId));

        var document = new Document(
            id,
            workspaceId,
            ownerUserId,
            title.Trim(),
            string.IsNullOrEmpty(content) ? LexicalJson.EmptyDocument : content,
            isPrivate,
            spaceId,
            listId,
            taskId,
            parentDocumentId,
            nowUtc);
        document._versions.Add(DocumentVersion.Create(id, id, ownerUserId, document.Content, nowUtc));
        return document;
    }

    /// <summary>Re-parents the document into the wiki tree. Callers MUST have already checked
    /// <see cref="DocumentHierarchy.CreatesCycle"/> against the workspace's current parent map — this
    /// method only guards against a document becoming its own direct parent.</summary>
    public void SetParent(Guid? newParentDocumentId, Guid editorUserId, DateTimeOffset nowUtc)
    {
        if (newParentDocumentId == Id)
        {
            throw new ValidationAppException("A document cannot be its own parent.");
        }

        if (ParentDocumentId != newParentDocumentId)
        {
            ParentDocumentId = newParentDocumentId;
            UpdatedAtUtc = nowUtc;
        }
    }

    public void Update(Guid newVersionId, string? title, string? content, bool? isPrivate, Guid editorUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(editorUserId, nameof(editorUserId));
        var changed = false;

        if (title is not null)
        {
            Guard.AgainstNullOrWhiteSpace(title, nameof(title));
            var trimmedTitle = title.Trim();
            if (Title != trimmedTitle)
            {
                Title = trimmedTitle;
                changed = true;
            }
        }

        if (content is not null && Content != content)
        {
            Content = content;
            _versions.Add(DocumentVersion.Create(newVersionId, Id, editorUserId, Content, nowUtc));
            changed = true;
        }

        if (isPrivate is not null && IsPrivate != isPrivate.Value)
        {
            IsPrivate = isPrivate.Value;
            changed = true;
        }

        if (changed)
        {
            UpdatedAtUtc = nowUtc;
        }
    }

    public void Revert(Guid newVersionId, DocumentVersion target, Guid editorUserId, DateTimeOffset nowUtc)
    {
        Guard.AgainstEmpty(editorUserId, nameof(editorUserId));
        Content = target.Content;
        _versions.Add(DocumentVersion.Create(newVersionId, Id, editorUserId, Content, nowUtc));
        UpdatedAtUtc = nowUtc;
    }

    public bool CanBeViewedBy(Guid userId) => !IsPrivate || OwnerUserId == userId;

    public void EnsureViewableBy(Guid userId)
    {
        if (!CanBeViewedBy(userId))
        {
            throw new ForbiddenException("This document is private to its owner.");
        }
    }
}

/// <summary>An immutable snapshot of a document's content at a point in time.</summary>
public sealed class DocumentVersion : Entity, IWorkspaceOwned
{
    public Guid WorkspaceId { get; private set; }

    private DocumentVersion()
    {
    }

    private DocumentVersion(Guid id, Guid documentId, Guid authorUserId, string content, DateTimeOffset createdAtUtc)
        : base(id)
    {
        DocumentId = documentId;
        AuthorUserId = authorUserId;
        Content = content;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid DocumentId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static DocumentVersion Create(Guid id, Guid documentId, Guid authorUserId, string content, DateTimeOffset createdAtUtc)
    {
        Guard.AgainstEmpty(documentId, nameof(documentId));
        Guard.AgainstEmpty(authorUserId, nameof(authorUserId));
        return new DocumentVersion(id, documentId, authorUserId, content ?? string.Empty, createdAtUtc);
    }
}

/// <summary>
/// Pure cycle-prevention for document wiki nesting, identical algorithm to the FolderHierarchy:
/// given the parent-document map of every document in a workspace, decides whether
/// re-parenting one document under another would make that document its own ancestor. No I/O — callers
/// load the map once via <c>IDocumentStore.ListParentMapByWorkspaceAsync</c> and pass it in.
/// </summary>
public static class DocumentHierarchy
{
    public static bool CreatesCycle(Guid documentId, Guid? newParentDocumentId, IReadOnlyDictionary<Guid, Guid?> parentById)
    {
        if (newParentDocumentId is null)
        {
            return false;
        }

        if (newParentDocumentId == documentId)
        {
            return true;
        }

        Guid? current = newParentDocumentId;
        var hops = 0;
        while (current is { } id)
        {
            if (id == documentId)
            {
                return true;
            }

            if (!parentById.TryGetValue(id, out var next))
            {
                return false;
            }

            current = next;

            // Defensive: bound the walk so a pre-existing data anomaly can never spin this into an
            // infinite loop (should be impossible given this same guard governs every write) — treat as
            // a cycle rather than looping forever.
            if (++hops > parentById.Count)
            {
                return true;
            }
        }

        return false;
    }
}
