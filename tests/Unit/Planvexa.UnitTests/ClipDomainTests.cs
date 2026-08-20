namespace Planvexa.UnitTests.Clips;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Clips.Application;
using Planvexa.Modules.Clips.Application.Services;
using Planvexa.Modules.Clips.Domain;
using Planvexa.SharedContracts.Ai;
using Planvexa.SharedContracts.Workspaces;
using Planvexa.UnitTests.Whiteboards;
using Shouldly;
using Xunit;

public sealed class ClipTests
{
    [Fact]
    public void Private_clip_is_only_viewable_by_its_owner()
    {
        var owner = Guid.CreateVersion7();
        var clip = Clip.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Standup recording", null, isPrivate: true, owner, "path", "video/webm", 1024, null, DateTimeOffset.UtcNow);

        clip.CanBeViewedBy(owner).ShouldBeTrue();
        clip.CanBeViewedBy(Guid.CreateVersion7()).ShouldBeFalse();
    }

    [Fact]
    public void CreateLinked_is_never_private_by_itself()
    {
        var clip = Clip.CreateLinked(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Demo", null, ClipLinkedResourceTypes.Document, Guid.CreateVersion7(), Guid.CreateVersion7(),
            "path", "video/webm", 1024, 12.5, DateTimeOffset.UtcNow);

        clip.IsPrivate.ShouldBeFalse();
        clip.CanBeViewedBy(Guid.CreateVersion7()).ShouldBeTrue();
    }

    [Fact]
    public void CreateLinked_rejects_an_unsupported_resource_type()
    {
        Should.Throw<ValidationAppException>(() =>
            Clip.CreateLinked(Guid.CreateVersion7(), Guid.CreateVersion7(), "Demo", null, "space", Guid.CreateVersion7(), Guid.CreateVersion7(), "path", "video/webm", 1024, null, DateTimeOffset.UtcNow));
    }
}

/// <summary>
/// The privacy-INHERITANCE proof for Clips, mirroring WhiteboardPrivacyInheritanceTests exactly: exercises
/// the real public <see cref="ClipService.GetAsync"/> path end to end against fakes, proving a Clip linked
/// to a Task/Document is exactly as visible as that Task/Document. See
/// Planvexa.IntegrationTests for the real-DB, real-ACL-resolver regression test of the identical rule.
/// </summary>
public sealed class ClipPrivacyInheritanceTests
{
    private static readonly Guid WorkspaceId = Guid.CreateVersion7();
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Viewer = Guid.CreateVersion7();
    private static readonly Guid LinkedDocumentId = Guid.CreateVersion7();

    [Fact]
    public async Task Linked_clip_is_hidden_when_the_viewer_cannot_see_the_linked_document()
    {
        var clip = Clip.CreateLinked(Guid.CreateVersion7(), WorkspaceId, "Walkthrough", null, ClipLinkedResourceTypes.Document, LinkedDocumentId, Owner, "path", "video/webm", 2048, 30, DateTimeOffset.UtcNow);
        var svc = BuildService(clip, linkedResourceGranted: false);

        await Should.ThrowAsync<ForbiddenException>(() => svc.GetAsync(clip.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Linked_clip_is_visible_once_the_viewer_can_see_the_linked_document()
    {
        var clip = Clip.CreateLinked(Guid.CreateVersion7(), WorkspaceId, "Walkthrough", null, ClipLinkedResourceTypes.Document, LinkedDocumentId, Owner, "path", "video/webm", 2048, 30, DateTimeOffset.UtcNow);
        var svc = BuildService(clip, linkedResourceGranted: true);

        var dto = await svc.GetAsync(clip.Id, CancellationToken.None);
        dto.Id.ShouldBe(clip.Id);
    }

    [Fact]
    public async Task Plain_private_clip_is_hidden_from_a_non_owner_regardless_of_linked_resource_access()
    {
        var clip = Clip.Create(Guid.CreateVersion7(), WorkspaceId, "My private clip", null, isPrivate: true, Owner, "path", "video/webm", 2048, null, DateTimeOffset.UtcNow);
        var svc = BuildService(clip, linkedResourceGranted: true);

        await Should.ThrowAsync<ForbiddenException>(() => svc.GetAsync(clip.Id, CancellationToken.None));
    }

    private static ClipService BuildService(Clip clip, bool linkedResourceGranted)
    {
        var store = new FakeClipStore(clip);
        var accessor = new WorkspaceContextAccessor();
        accessor.Set(new WorkspaceContext(WorkspaceId, Viewer, null, "Member", new HashSet<string>(), new HashSet<string>(), "test"));

        var ctx = new ClipServiceContext(
            accessor,
            new FakeCurrentUser(Viewer),
            new FakeIdGenerator(),
            new FakeClock(),
            new FakeAuditWriter(),
            new FakeWorkspaceAccessQuery(WorkspaceRole.Member),
            new FakeLinkedResourceAccessQuery(linkedResourceGranted),
            new FakeFileStorage(),
            new FakeMalwareScanner(),
            new FakeClipTranscriber(),
            new FakeUnitOfWork());

        return new ClipService(ctx, store);
    }

    private sealed class FakeClipStore(Clip clip) : IClipStore
    {
        public void Add(Clip c)
        {
        }

        public void Remove(Clip c)
        {
        }

        public Task<Clip?> FindAsync(Guid id, CancellationToken ct = default) => Task.FromResult(id == clip.Id ? clip : null);

        public Task<IReadOnlyList<Clip>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Clip>>([clip]);
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public Task SaveAsync(string path, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GetSignedDownloadUrlAsync(string path, TimeSpan expiry, CancellationToken cancellationToken = default) => Task.FromResult("https://example.test/download");

        public Task<string> GetSignedUploadUrlAsync(string path, string contentType, TimeSpan expiry, CancellationToken cancellationToken = default) => Task.FromResult("https://example.test/upload");
    }

    private sealed class FakeMalwareScanner : IMalwareScanner
    {
        public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default) => Task.FromResult(MalwareScanResult.Clean);
    }

    private sealed class FakeClipTranscriber : IClipTranscriber
    {
        public Task<ClipTranscriptionResult?> TranscribeAsync(Guid workspaceId, Stream audio, string fileName, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult<ClipTranscriptionResult?>(null);
    }
}
