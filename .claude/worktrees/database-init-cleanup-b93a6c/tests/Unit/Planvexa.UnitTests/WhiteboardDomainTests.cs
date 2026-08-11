namespace Planvexa.UnitTests.Whiteboards;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Whiteboards.Application;
using Planvexa.Modules.Whiteboards.Application.Services;
using Planvexa.Modules.Whiteboards.Domain;
using Planvexa.SharedContracts.Workspaces;
using Shouldly;
using Xunit;

public sealed class WhiteboardTests
{
    [Fact]
    public void Create_defaults_to_no_linked_resource()
    {
        var owner = Guid.CreateVersion7();
        var wb = Whiteboard.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Board", isPrivate: false, owner, DateTimeOffset.UtcNow);
        wb.LinkedResourceType.ShouldBeNull();
        wb.LinkedResourceId.ShouldBeNull();
    }

    [Fact]
    public void Private_whiteboard_is_only_viewable_by_its_owner()
    {
        var owner = Guid.CreateVersion7();
        var wb = Whiteboard.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Board", isPrivate: true, owner, DateTimeOffset.UtcNow);

        wb.CanBeViewedBy(owner).ShouldBeTrue();
        wb.CanBeViewedBy(Guid.CreateVersion7()).ShouldBeFalse();
    }

    [Fact]
    public void Public_whiteboard_is_viewable_by_anyone()
    {
        var wb = Whiteboard.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Board", isPrivate: false, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        wb.CanBeViewedBy(Guid.CreateVersion7()).ShouldBeTrue();
    }

    [Fact]
    public void CreateLinked_is_never_private_by_itself()
    {
        var wb = Whiteboard.CreateLinked(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Board", WhiteboardLinkedResourceTypes.Task, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        wb.IsPrivate.ShouldBeFalse();
        // The structural check alone says "yes" for any user — the real gate for a linked whiteboard is the
        // linked resource's ACL, applied asynchronously by WhiteboardService, not the entity itself.
        wb.CanBeViewedBy(Guid.CreateVersion7()).ShouldBeTrue();
    }

    [Fact]
    public void CreateLinked_rejects_an_unsupported_resource_type()
    {
        Should.Throw<ValidationAppException>(() =>
            Whiteboard.CreateLinked(Guid.CreateVersion7(), Guid.CreateVersion7(), "Board", "space", Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UpdateDetails_ignores_isPrivate_for_a_linked_whiteboard()
    {
        var wb = Whiteboard.CreateLinked(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Board", WhiteboardLinkedResourceTypes.Document, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        wb.UpdateDetails(null, true, DateTimeOffset.UtcNow);

        // A linked whiteboard's visibility always tracks the linked resource — it must not be toggled
        // directly via a generic metadata update.
        wb.IsPrivate.ShouldBeFalse();
    }
}

/// <summary>
/// The privacy-INHERITANCE proof (the explicit ask): exercises the real public
/// <see cref="WhiteboardService.GetAsync"/> path end to end against fakes (no DB — see
/// Planvexa.IntegrationTests for the real-DB, real-ACL-resolver regression test of the identical rule).
/// A Whiteboard linked to a Task/Document must be exactly as visible as that Task/Document: this is proven
/// by driving <see cref="FakeLinkedResourceAccessQuery"/>'s answer and observing WhiteboardService's real
/// authorization outcome change accordingly.
/// </summary>
public sealed class WhiteboardPrivacyInheritanceTests
{
    private static readonly Guid WorkspaceId = Guid.CreateVersion7();
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Viewer = Guid.CreateVersion7();
    private static readonly Guid LinkedTaskId = Guid.CreateVersion7();

    [Fact]
    public async Task Linked_whiteboard_is_hidden_when_the_viewer_cannot_see_the_linked_task()
    {
        var wb = Whiteboard.CreateLinked(Guid.CreateVersion7(), WorkspaceId, "Sprint board", WhiteboardLinkedResourceTypes.Task, LinkedTaskId, Owner, DateTimeOffset.UtcNow);
        var svc = BuildService(wb, linkedResourceGranted: false);

        await Should.ThrowAsync<ForbiddenException>(() => svc.GetAsync(wb.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Linked_whiteboard_is_visible_once_the_viewer_can_see_the_linked_task()
    {
        var wb = Whiteboard.CreateLinked(Guid.CreateVersion7(), WorkspaceId, "Sprint board", WhiteboardLinkedResourceTypes.Task, LinkedTaskId, Owner, DateTimeOffset.UtcNow);
        var svc = BuildService(wb, linkedResourceGranted: true);

        var dto = await svc.GetAsync(wb.Id, CancellationToken.None);
        dto.Id.ShouldBe(wb.Id);
    }

    [Fact]
    public async Task Plain_private_whiteboard_is_hidden_from_a_non_owner_regardless_of_linked_resource_access()
    {
        // Not a linked whiteboard at all — the linked-resource fake would happily say "yes" for anything,
        // proving the private-owner rule is still enforced independently.
        var wb = Whiteboard.Create(Guid.CreateVersion7(), WorkspaceId, "My scratch board", isPrivate: true, Owner, DateTimeOffset.UtcNow);
        var svc = BuildService(wb, linkedResourceGranted: true);

        await Should.ThrowAsync<ForbiddenException>(() => svc.GetAsync(wb.Id, CancellationToken.None));
    }

    private static WhiteboardService BuildService(Whiteboard wb, bool linkedResourceGranted)
    {
        var store = new FakeWhiteboardStore(wb);
        var accessor = new WorkspaceContextAccessor();
        accessor.Set(new WorkspaceContext(WorkspaceId, Viewer, null, "Member", new HashSet<string>(), new HashSet<string>(), "test"));

        var ctx = new WhiteboardServiceContext(
            accessor,
            new FakeCurrentUser(Viewer),
            new FakeIdGenerator(),
            new FakeClock(),
            new FakeAuditWriter(),
            new FakeWorkspaceAccessQuery(WorkspaceRole.Member),
            new FakeLinkedResourceAccessQuery(linkedResourceGranted),
            new FakeFileStorage(),
            new FakeMalwareScanner(),
            new FakeUnitOfWork());

        return new WhiteboardService(ctx, store, new FakeWhiteboardTemplateStore(), new FakeWhiteboardCollabStateStore());
    }

    private sealed class FakeWhiteboardStore(Whiteboard whiteboard) : IWhiteboardStore
    {
        public void Add(Whiteboard w)
        {
        }

        public void Remove(Whiteboard w)
        {
        }

        public Task<Whiteboard?> FindAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(id == whiteboard.Id ? whiteboard : null);

        public Task<IReadOnlyList<Whiteboard>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Whiteboard>>([whiteboard]);
    }

    private sealed class FakeWhiteboardTemplateStore : IWhiteboardTemplateStore
    {
        public void Add(WhiteboardTemplate template)
        {
        }

        public Task<WhiteboardTemplate?> FindAsync(Guid id, CancellationToken ct = default) => Task.FromResult<WhiteboardTemplate?>(null);

        public Task<IReadOnlyList<WhiteboardTemplate>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WhiteboardTemplate>>([]);
    }

    private sealed class FakeWhiteboardCollabStateStore : IWhiteboardCollabStateStore
    {
        public Task<byte[]?> GetStateAsync(Guid whiteboardId, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);

        public Task SeedAsync(Guid whiteboardId, Guid workspaceId, byte[] state, CancellationToken ct = default) => Task.CompletedTask;
    }
}

/// <summary>Answers every check the same way — set per test to prove WhiteboardService's outcome tracks it.</summary>
internal sealed class FakeLinkedResourceAccessQuery(bool granted) : ILinkedResourceAccessQuery
{
    public Task<bool> CanViewAsync(Guid workspaceId, Guid userId, string resourceType, Guid resourceId, CancellationToken cancellationToken = default)
        => Task.FromResult(granted);
}

internal sealed class FakeCurrentUser(Guid userId) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid UserId => userId;
    public string Subject => userId.ToString();
    public string Email => "test@planvexa.test";
    public string DisplayName => "Test User";
}

internal sealed class FakeIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7();
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

internal sealed class FakeAuditWriter : IAuditWriter
{
    public void Write(string action, string entityType, Guid? entityId = null, object? data = null, string? ipAddress = null)
    {
    }
}

internal sealed class FakeWorkspaceAccessQuery(WorkspaceRole role) : IWorkspaceAccessQuery
{
    public Task<WorkspaceAccess?> GetAccessAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<WorkspaceAccess?>(new WorkspaceAccess(workspaceId, userId, role, IsGuest: false));
}

internal sealed class FakeUnitOfWork : Planvexa.BuildingBlocks.Domain.IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}

internal sealed class FakeFileStorage : IFileStorage
{
    public Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string> GetSignedDownloadUrlAsync(string path, TimeSpan expiry, CancellationToken cancellationToken = default) => Task.FromResult("https://example.test/download");

    public Task<string> GetSignedUploadUrlAsync(string path, string contentType, TimeSpan expiry, CancellationToken cancellationToken = default) => Task.FromResult("https://example.test/upload");
}

internal sealed class FakeMalwareScanner : IMalwareScanner
{
    public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default) => Task.FromResult(MalwareScanResult.Clean);
}
