namespace Planvexa.Modules.Whiteboards.Application;

using Planvexa.Modules.Whiteboards.Domain;

public interface IWhiteboardStore
{
    void Add(Whiteboard whiteboard);
    void Remove(Whiteboard whiteboard);
    Task<Whiteboard?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Whiteboard>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

public interface IWhiteboardTemplateStore
{
    void Add(WhiteboardTemplate template);
    Task<WhiteboardTemplate?> FindAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WhiteboardTemplate>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
}

/// <summary>
/// Bridge to the Yjs collaboration state apps/collaboration owns (see Whiteboard's doc comment). Only
/// used for template capture/seed — the Node service is otherwise the sole reader/writer of this table.
/// </summary>
public interface IWhiteboardCollabStateStore
{
    Task<byte[]?> GetStateAsync(Guid whiteboardId, CancellationToken ct = default);

    Task SeedAsync(Guid whiteboardId, Guid workspaceId, byte[] state, CancellationToken ct = default);
}
