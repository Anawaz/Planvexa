namespace Planvexa.UnitTests.WorkManagement;

using Planvexa.Modules.WorkManagement.Domain;
using Shouldly;
using Xunit;

/// <summary>(task management completeness) domain-level unit tests: team assignees, task types,
/// custom ids, generic relations, and the rich-text description JSON wrapper. Multi-list membership's
/// privacy resolution is covered in ResourcePermissionServiceTests (GetEffectiveViaAsync); the
/// integration suite covers the full end-to-end multi-list + privacy scenario.</summary>
public sealed class TaskManagementTests
{
    private static (Guid ws, Guid space, Guid list, Guid status) Ids()
        => (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    private static WorkItem NewTask()
    {
        var (ws, space, list, status) = Ids();
        return WorkItem.Create(Guid.CreateVersion7(), ws, space, list, null, 1, "Task", status, false, 1024, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Team_assignees_are_unique_and_toggle_independently_of_user_assignees()
    {
        var task = NewTask();
        var actor = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        task.AddTeamAssignee(Guid.CreateVersion7(), teamId, actor, DateTimeOffset.UtcNow).ShouldBeTrue();
        task.AddTeamAssignee(Guid.CreateVersion7(), teamId, actor, DateTimeOffset.UtcNow).ShouldBeFalse(); // no duplicate
        task.AddAssignee(Guid.CreateVersion7(), userId, actor, DateTimeOffset.UtcNow).ShouldBeTrue();

        task.TeamAssignees.Count.ShouldBe(1);
        task.Assignees.Count.ShouldBe(1); // independent collections

        task.RemoveTeamAssignee(teamId, actor, DateTimeOffset.UtcNow).ShouldBeTrue();
        task.TeamAssignees.ShouldBeEmpty();
        task.Assignees.Count.ShouldBe(1); // removing a team assignee does not touch user assignees
    }

    [Fact]
    public void SetTaskType_and_SetCustomId_update_the_task()
    {
        var task = NewTask();
        var actor = Guid.CreateVersion7();
        var typeId = Guid.CreateVersion7();

        task.TaskTypeId.ShouldBeNull();
        task.SetTaskType(typeId, actor, DateTimeOffset.UtcNow);
        task.TaskTypeId.ShouldBe(typeId);

        task.SetCustomId("BUG-123", actor, DateTimeOffset.UtcNow);
        task.CustomId.ShouldBe("BUG-123");

        // Blank/whitespace clears it back to null (Guard-free, matches Tag/Team's Normalize pattern).
        task.SetCustomId("   ", actor, DateTimeOffset.UtcNow);
        task.CustomId.ShouldBeNull();
    }

    [Fact]
    public void TaskType_CreateBuiltIn_is_named_Task_and_flagged_built_in()
    {
        var workspaceId = Guid.CreateVersion7();
        var builtIn = TaskType.CreateBuiltIn(Guid.CreateVersion7(), workspaceId);

        builtIn.Name.ShouldBe("Task");
        builtIn.IsBuiltIn.ShouldBeTrue();
        builtIn.WorkspaceId.ShouldBe(workspaceId);
    }

    [Fact]
    public void TaskType_Create_trims_the_name_and_defaults_the_color()
    {
        var type = TaskType.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "  Bug  ", null, "bug-icon", 0);

        type.Name.ShouldBe("Bug");
        type.Color.ShouldBe("#8b8b8b");
        type.IsBuiltIn.ShouldBeFalse();
    }

    [Fact]
    public void TaskRelation_stores_both_task_ids_so_either_side_can_resolve_the_other()
    {
        var taskA = Guid.CreateVersion7();
        var taskB = Guid.CreateVersion7();
        var relation = new TaskRelation(Guid.CreateVersion7(), taskA, taskB, DateTimeOffset.UtcNow);

        relation.TaskId.ShouldBe(taskA);
        relation.RelatedTaskId.ShouldBe(taskB);

        // Whichever side of the pair a caller queries from, they can resolve "the other task" — this
        // is what lets ITaskRelationStore.ListForTaskAsync match on TaskId OR RelatedTaskId (see
        // WorkMapper.ToRelationDto, exercised end-to-end by the integration suite's relation test).
        var otherFromA = relation.TaskId == taskA ? relation.RelatedTaskId : relation.TaskId;
        var otherFromB = relation.TaskId == taskB ? relation.RelatedTaskId : relation.TaskId;
        otherFromA.ShouldBe(taskB);
        otherFromB.ShouldBe(taskA);
    }

    [Fact]
    public void ReassignTask_moves_a_checklist_attachment_and_custom_field_value_to_a_new_task_id()
    {
        var oldTaskId = Guid.CreateVersion7();
        var newTaskId = Guid.CreateVersion7();

        var checklist = TaskChecklist.Create(Guid.CreateVersion7(), oldTaskId, "Steps", 1024);
        checklist.ReassignTask(newTaskId);
        checklist.TaskId.ShouldBe(newTaskId);

        var attachment = new TaskAttachment(
            Guid.CreateVersion7(), Guid.CreateVersion7(), oldTaskId, "file.txt", "text/plain", 10, "path", Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        attachment.ReassignTask(newTaskId);
        attachment.TaskId.ShouldBe(newTaskId);

        var value = CustomFieldValue.Create(Guid.CreateVersion7(), oldTaskId, Guid.CreateVersion7());
        value.ReassignTask(newTaskId);
        value.TaskId.ShouldBe(newTaskId);
    }
}

/// <summary>The ProseMirror/Lexical-shaped JSON wrapper WorkItem.Description is persisted as.</summary>
public sealed class DescriptionJsonTests
{
    [Fact]
    public void Empty_or_null_text_round_trips_to_null()
    {
        DescriptionJson.ToJson(null).ShouldBeNull();
        DescriptionJson.ToJson(string.Empty).ShouldBeNull();
        DescriptionJson.FromText(null).ShouldBeNull();
    }

    [Fact]
    public void Plain_text_round_trips_through_the_doc_wrapper_unchanged()
    {
        const string original = "Some plain description text.";
        var json = DescriptionJson.ToJson(original);

        json.ShouldNotBeNull();
        json.ShouldContain("\"type\":\"doc\"");
        json.ShouldContain("\"type\":\"paragraph\"");
        json.ShouldContain("\"type\":\"text\"");

        DescriptionJson.FromText(json).ShouldBe(original);
    }

    [Fact]
    public void Malformed_json_is_returned_verbatim_instead_of_dropping_content()
    {
        const string notJson = "this is not json";
        DescriptionJson.FromText(notJson).ShouldBe(notJson);
    }
}
