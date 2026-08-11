namespace Planvexa.UnitTests.WorkManagement;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.Modules.WorkManagement.Authorization;
using Planvexa.Modules.WorkManagement.Domain;
using Planvexa.SharedContracts.Workspaces;
using Shouldly;
using Xunit;

public sealed class WorkAuthorizationTests
{
    [Theory]
    [InlineData(WorkspaceRole.Guest, false, false)]
    [InlineData(WorkspaceRole.Member, true, false)]
    [InlineData(WorkspaceRole.Admin, true, true)]
    [InlineData(WorkspaceRole.Owner, true, true)]
    public void Role_capabilities(WorkspaceRole role, bool canEdit, bool canManage)
    {
        WorkManagementAuthorizer.CanRead(role).ShouldBeTrue();
        WorkManagementAuthorizer.CanEditContent(role).ShouldBe(canEdit);
        WorkManagementAuthorizer.CanManageStructure(role).ShouldBe(canManage);
    }

    [Fact]
    public void Null_access_is_denied_everywhere()
    {
        WorkManagementAuthorizer.CanRead(null).ShouldBeFalse();
        Should.Throw<ForbiddenException>(() => WorkManagementAuthorizer.EnsureRead(null));
        Should.Throw<ForbiddenException>(() => WorkManagementAuthorizer.EnsureEditContent(WorkspaceRole.Guest));
        Should.Throw<ForbiddenException>(() => WorkManagementAuthorizer.EnsureManageStructure(WorkspaceRole.Member));
    }
}

public sealed class WorkItemDomainTests
{
    private static (Guid ws, Guid space, Guid list, Guid status) Ids()
        => (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public void Create_raises_task_created_and_defaults_priority()
    {
        var (ws, space, list, status) = Ids();
        var task = WorkItem.Create(Guid.CreateVersion7(), ws, space, list, null, 1, "Do the thing", status, false, 1024, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        task.Priority.ShouldBe(TaskPriority.None);
        task.IsCompleted.ShouldBeFalse();
        task.DomainEvents.ShouldNotBeEmpty();
    }

    [Fact]
    public void Create_stores_the_idempotency_key_when_supplied()
    {
        var (ws, space, list, status) = Ids();
        var task = WorkItem.Create(
            Guid.CreateVersion7(), ws, space, list, null, 1, "Offline-created task", status, false, 1024,
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, idempotencyKey: "outbox-key-1");

        task.IdempotencyKey.ShouldBe("outbox-key-1");
    }

    [Fact]
    public void Create_leaves_the_idempotency_key_null_when_not_supplied()
    {
        var (ws, space, list, status) = Ids();
        var task = WorkItem.Create(Guid.CreateVersion7(), ws, space, list, null, 1, "Do the thing", status, false, 1024, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        task.IdempotencyKey.ShouldBeNull();
    }

    [Fact]
    public void Complete_throws_when_a_blocker_is_incomplete()
    {
        var (ws, space, list, status) = Ids();
        var doneStatus = Guid.CreateVersion7();
        var task = WorkItem.Create(Guid.CreateVersion7(), ws, space, list, null, 1, "Blocked task", status, false, 1024, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Should.Throw<ConflictException>(() =>
            task.Complete(doneStatus, hasIncompleteBlocker: true, Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void Complete_succeeds_when_not_blocked_and_sets_completion_state()
    {
        var (ws, space, list, status) = Ids();
        var doneStatus = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        var task = WorkItem.Create(Guid.CreateVersion7(), ws, space, list, null, 1, "Task", status, false, 1024, actor, DateTimeOffset.UtcNow);

        task.Complete(doneStatus, hasIncompleteBlocker: false, actor, DateTimeOffset.UtcNow);

        task.IsCompleted.ShouldBeTrue();
        task.StatusId.ShouldBe(doneStatus);
        task.CompletedByUserId.ShouldBe(actor);
        task.CompletedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void Assignees_are_unique_and_toggle()
    {
        var (ws, space, list, status) = Ids();
        var actor = Guid.CreateVersion7();
        var assignee = Guid.CreateVersion7();
        var task = WorkItem.Create(Guid.CreateVersion7(), ws, space, list, null, 1, "Task", status, false, 1024, actor, DateTimeOffset.UtcNow);

        task.AddAssignee(Guid.CreateVersion7(), assignee, actor, DateTimeOffset.UtcNow).ShouldBeTrue();
        task.AddAssignee(Guid.CreateVersion7(), assignee, actor, DateTimeOffset.UtcNow).ShouldBeFalse();
        task.Assignees.Count.ShouldBe(1);

        task.RemoveAssignee(assignee, actor, DateTimeOffset.UtcNow).ShouldBeTrue();
        task.Assignees.ShouldBeEmpty();
    }

    [Fact]
    public void Removing_a_tag_not_present_on_the_task_is_a_no_op()
    {
        // Mirrors TaskWriteApi.RemoveTagByNameAsync's idempotent-no-op path: when the tag to remove
        // isn't among the task's current tags, the desired id set it computes (current ids minus the
        // absent tag) is unchanged, so SetTags must leave the existing tag row untouched rather than
        // dropping and re-adding it.
        var (ws, space, list, status) = Ids();
        var actor = Guid.CreateVersion7();
        var task = WorkItem.Create(Guid.CreateVersion7(), ws, space, list, null, 1, "Task", status, false, 1024, actor, DateTimeOffset.UtcNow);

        var keptTag = Guid.CreateVersion7();
        task.SetTags([keptTag], Guid.CreateVersion7, actor, DateTimeOffset.UtcNow);
        var rowIdBefore = task.Tags.Single().Id;

        var absentTag = Guid.CreateVersion7();
        var desiredIds = task.Tags.Select(t => t.TagId).Where(id => id != absentTag).ToHashSet();
        task.SetTags(desiredIds, Guid.CreateVersion7, actor, DateTimeOffset.UtcNow);

        task.Tags.Select(t => t.TagId).ShouldBe([keptTag]);
        task.Tags.Single().Id.ShouldBe(rowIdBefore);
    }

    [Fact]
    public void Default_status_prefers_not_started_then_position()
    {
        var scheme = StatusScheme.CreateDefault(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7);
        scheme.DefaultStatus().Category.ShouldBe(StatusCategory.NotStarted);
        scheme.Statuses.Count(s => s.IsCompletedCategory).ShouldBe(1);
    }

    [Fact]
    public void A_status_with_no_configured_transitions_permits_any_move()
    {
        var scheme = StatusScheme.CreateDefault(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7);
        var (toDo, done) = (scheme.Statuses[0], scheme.Statuses[^1]);

        scheme.CanTransition(toDo.Id, done.Id).ShouldBeTrue();
        scheme.CanTransition(done.Id, toDo.Id).ShouldBeTrue();
    }

    [Fact]
    public void Configured_transitions_reject_any_target_not_on_the_allow_list()
    {
        var scheme = StatusScheme.CreateDefault(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7);
        var (toDo, inProgress, inReview, done) = (scheme.Statuses[0], scheme.Statuses[1], scheme.Statuses[2], scheme.Statuses[3]);

        // To Do -> only In Progress.
        scheme.SetAllowedTransitions(toDo.Id, [inProgress.Id]);

        scheme.CanTransition(toDo.Id, inProgress.Id).ShouldBeTrue();
        scheme.CanTransition(toDo.Id, done.Id).ShouldBeFalse();
        scheme.CanTransition(toDo.Id, inReview.Id).ShouldBeFalse();

        // Untouched statuses stay unrestricted.
        scheme.CanTransition(inProgress.Id, done.Id).ShouldBeTrue();

        // Clearing the restriction (empty list) makes it unrestricted again.
        scheme.SetAllowedTransitions(toDo.Id, []);
        scheme.CanTransition(toDo.Id, done.Id).ShouldBeTrue();
    }

    [Fact]
    public void Setting_transitions_rejects_targets_outside_the_scheme_and_self_transitions()
    {
        var scheme = StatusScheme.CreateDefault(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7);
        var toDo = scheme.Statuses[0];
        var foreignStatusId = Guid.CreateVersion7();

        Should.Throw<ValidationAppException>(() => scheme.SetAllowedTransitions(toDo.Id, [foreignStatusId]));
        Should.Throw<ValidationAppException>(() => scheme.SetAllowedTransitions(toDo.Id, [toDo.Id]));
        Should.Throw<ValidationAppException>(() => scheme.SetAllowedTransitions(foreignStatusId, [scheme.Statuses[1].Id]));
    }

    [Fact]
    public void A_from_status_that_does_not_belong_to_this_scheme_is_treated_as_unrestricted()
    {
        // Simulates a cross-list move into a list with a different workflow: the task's current status
        // id is not one of THIS scheme's statuses, so it must not be rejected as "not on the allow list".
        var scheme = StatusScheme.CreateDefault(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7);
        var statusFromAnotherScheme = Guid.CreateVersion7();

        scheme.CanTransition(statusFromAnotherScheme, scheme.Statuses[0].Id).ShouldBeTrue();
    }
}
