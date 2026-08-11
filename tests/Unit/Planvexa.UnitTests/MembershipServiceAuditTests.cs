namespace Planvexa.UnitTests.Tenancy;

using Planvexa.BuildingBlocks.Abstractions;
using Planvexa.BuildingBlocks.Domain;
using Planvexa.BuildingBlocks.Workspaces;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Authorization;
using Planvexa.Modules.Tenancy.Domain;
using Shouldly;
using Xunit;

/// <summary>
/// Security-sensitive audit payloads must capture both sides of a mutation, not just the new value —
/// otherwise the audit log can't answer "what was it before?".
/// </summary>
public sealed class MembershipServiceAuditTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    [Fact]
    public async Task ChangeRoleAsync_audits_both_the_previous_and_new_role()
    {
        var workspace = Workspace.Create(WorkspaceId, "Acme", "acme", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var callerId = Guid.NewGuid();
        var caller = WorkspaceMember.Create(Guid.NewGuid(), WorkspaceId, callerId, MembershipRole.Owner, DateTimeOffset.UtcNow);
        var target = WorkspaceMember.Create(Guid.NewGuid(), WorkspaceId, Guid.NewGuid(), MembershipRole.Member, DateTimeOffset.UtcNow);

        var memberships = new FakeMembershipStore(caller, target);
        var audit = new CapturingAuditWriter();

        var accessor = new WorkspaceContextAccessor();
        accessor.Set(new WorkspaceContext(
            WorkspaceId, callerId, caller.Id, "Owner", new HashSet<string>(), new HashSet<string>(), "corr"));

        var svc = new MembershipService(
            accessor,
            memberships,
            new FakeWorkspaceStore(workspace),
            new FakeRoleStore(),
            new RolePermissionResolver(new FakeRoleStore()),
            audit,
            new FakeUnitOfWork());

        await svc.ChangeRoleAsync(new ChangeMemberRoleCommand(WorkspaceId, target.Id, MembershipRole.Admin));

        var entry = audit.Entries.Single(e => e.Action == "member.role_changed");
        entry.GetString("previousRole").ShouldBe("Member");
        entry.GetString("newRole").ShouldBe("Admin");
    }

    private sealed class FakeMembershipStore(WorkspaceMember caller, WorkspaceMember target) : IMembershipStore
    {
        public void Add(WorkspaceMember member) => throw new NotSupportedException();
        public void Remove(WorkspaceMember member) => throw new NotSupportedException();

        public Task<WorkspaceMember?> FindAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(userId == caller.UserId ? caller : null);

        public Task<WorkspaceMember?> FindByIdAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default)
            => Task.FromResult(membershipId == target.Id ? target : membershipId == caller.Id ? caller : null);

        public Task<IReadOnlyList<WorkspaceMember>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountActiveOwnersAsync(Guid workspaceId, CancellationToken cancellationToken = default)
            => Task.FromResult(2); // more than one Owner — demotion path in tests here never targets the caller anyway

        public Task<IReadOnlyList<Guid>> ListWorkspaceIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeWorkspaceStore(Workspace workspace) : IWorkspaceStore
    {
        public void Add(Workspace ws) => throw new NotSupportedException();
        public Task<Workspace?> FindByIdAsync(Guid workspaceId, CancellationToken cancellationToken = default)
            => Task.FromResult(workspaceId == workspace.Id ? workspace : null);
        public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRoleStore : IRoleStore
    {
        public void Add(Role role) => throw new NotSupportedException();
        public void AddPermission(RolePermission permission) => throw new NotSupportedException();
        public Task<Role?> FindByIdAsync(Guid workspaceId, Guid roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Role?> FindByKeyAsync(Guid workspaceId, string key, CancellationToken cancellationToken = default) => Task.FromResult<Role?>(null);
        public Task<IReadOnlyList<Role>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetPermissionKeysAsync(Guid roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RoleWithPermissions>> ListWithPermissionsAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}

/// <summary>Shared no-DB <see cref="IAuditWriter"/> fake that records every call for assertions.</summary>
internal sealed class CapturingAuditWriter : IAuditWriter
{
    public List<CapturedAuditEntry> Entries { get; } = new();

    public void Write(string action, string entityType, Guid? entityId = null, object? data = null, string? ipAddress = null)
        => Entries.Add(new CapturedAuditEntry(action, entityType, entityId, data));
}

internal sealed record CapturedAuditEntry(string Action, string EntityType, Guid? EntityId, object? Data)
{
    public string GetString(string propertyName)
        => Data!.GetType().GetProperty(propertyName)!.GetValue(Data)!.ToString()!;
}
