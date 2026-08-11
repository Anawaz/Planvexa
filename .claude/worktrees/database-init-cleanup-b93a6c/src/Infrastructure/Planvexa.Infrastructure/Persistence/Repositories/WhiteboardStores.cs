namespace Planvexa.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.Modules.Whiteboards;
using Planvexa.Modules.Whiteboards.Application;
using Planvexa.Modules.Whiteboards.Domain;

internal sealed class WhiteboardStore(PlanvexaDbContext db) : IWhiteboardStore
{
    public void Add(Whiteboard whiteboard) => db.Set<Whiteboard>().Add(whiteboard);

    public void Remove(Whiteboard whiteboard) => db.Set<Whiteboard>().Remove(whiteboard);

    public Task<Whiteboard?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<Whiteboard>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Whiteboard>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<Whiteboard>()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(ct);
}

internal sealed class WhiteboardTemplateStore(PlanvexaDbContext db) : IWhiteboardTemplateStore
{
    public void Add(WhiteboardTemplate template) => db.Set<WhiteboardTemplate>().Add(template);

    public Task<WhiteboardTemplate?> FindAsync(Guid id, CancellationToken ct = default)
        => db.Set<WhiteboardTemplate>().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<WhiteboardTemplate>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
        => await db.Set<WhiteboardTemplate>()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
}

/// <summary>
/// Bridge row for apps/collaboration's Yjs state table (see Whiteboard's doc comment). Lives in
/// Infrastructure rather than the Whiteboards module's own Domain because it isn't a domain concept — it's
/// a foreign-owned persistence detail the module only ever touches through
/// <see cref="IWhiteboardCollabStateStore"/>, never directly.
/// </summary>
internal sealed class WhiteboardCollabStateRow : IWorkspaceOwned
{
    public Guid WhiteboardId { get; set; }
    public Guid WorkspaceId { get; set; }
    public byte[] YState { get; set; } = [];
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class WhiteboardCollabStateRowConfiguration : IEntityTypeConfiguration<WhiteboardCollabStateRow>
{
    public void Configure(EntityTypeBuilder<WhiteboardCollabStateRow> b)
    {
        b.ToTable("whiteboard_collab_state", WhiteboardsModule.Schema);
        b.HasKey(x => x.WhiteboardId);
        b.Property(x => x.WhiteboardId).HasColumnName("whiteboard_id").ValueGeneratedNever();
        b.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
        b.Property(x => x.YState).HasColumnName("y_state").IsRequired();
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
    }
}

internal sealed class WhiteboardCollabStateStore(PlanvexaDbContext db) : IWhiteboardCollabStateStore
{
    public async Task<byte[]?> GetStateAsync(Guid whiteboardId, CancellationToken ct = default)
        => (await db.Set<WhiteboardCollabStateRow>().AsNoTracking().FirstOrDefaultAsync(x => x.WhiteboardId == whiteboardId, ct))?.YState;

    public async Task SeedAsync(Guid whiteboardId, Guid workspaceId, byte[] state, CancellationToken ct = default)
    {
        var existing = await db.Set<WhiteboardCollabStateRow>().FirstOrDefaultAsync(x => x.WhiteboardId == whiteboardId, ct);
        if (existing is null)
        {
            db.Set<WhiteboardCollabStateRow>().Add(new WhiteboardCollabStateRow
            {
                WhiteboardId = whiteboardId,
                WorkspaceId = workspaceId,
                YState = state,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.YState = state;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }
}
