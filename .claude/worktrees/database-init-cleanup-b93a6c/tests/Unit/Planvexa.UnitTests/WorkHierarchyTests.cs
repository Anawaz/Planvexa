namespace Planvexa.UnitTests.WorkManagement;

using Planvexa.Modules.WorkManagement.Domain;
using Shouldly;
using Xunit;

/// <summary>Pure cycle prevention for arbitrary-depth folder re-parenting.</summary>
public sealed class FolderHierarchyTests
{
    [Fact]
    public void Moving_a_folder_under_itself_is_a_cycle()
    {
        var folderId = Guid.CreateVersion7();
        FolderHierarchy.CreatesCycle(folderId, folderId, new Dictionary<Guid, Guid?>()).ShouldBeTrue();
    }

    [Fact]
    public void Moving_a_folder_under_its_own_descendant_is_a_cycle()
    {
        var root = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        var grandchild = Guid.CreateVersion7();

        // root -> child -> grandchild
        var parentById = new Dictionary<Guid, Guid?>
        {
            [child] = root,
            [grandchild] = child,
        };

        // Moving root under its own grandchild would make root its own ancestor.
        FolderHierarchy.CreatesCycle(root, grandchild, parentById).ShouldBeTrue();
    }

    [Fact]
    public void Moving_a_folder_under_an_unrelated_folder_is_not_a_cycle()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var parentById = new Dictionary<Guid, Guid?> { [a] = null, [b] = null };

        FolderHierarchy.CreatesCycle(a, b, parentById).ShouldBeFalse();
    }

    [Fact]
    public void Moving_a_folder_to_top_level_is_never_a_cycle()
    {
        FolderHierarchy.CreatesCycle(Guid.CreateVersion7(), null, new Dictionary<Guid, Guid?>()).ShouldBeFalse();
    }

    [Fact]
    public void Deep_chains_are_still_detected()
    {
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.CreateVersion7()).ToList();
        var parentById = new Dictionary<Guid, Guid?>();
        for (var i = 1; i < ids.Count; i++)
        {
            parentById[ids[i]] = ids[i - 1];
        }

        // Moving the root (ids[0]) under the deepest descendant is a cycle.
        FolderHierarchy.CreatesCycle(ids[0], ids[^1], parentById).ShouldBeTrue();

        // Moving the deepest descendant under the root is fine (that is just normal re-parenting).
        FolderHierarchy.CreatesCycle(ids[^1], ids[0], parentById).ShouldBeFalse();
    }
}

/// <summary>Pure parent-id remapping used by Folder/List duplicate.</summary>
public sealed class TaskDuplicationMappingTests
{
    [Fact]
    public void Every_source_id_gets_a_fresh_new_id()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var map = TaskDuplicationMapping.BuildIdMap([a, b], Guid.CreateVersion7);

        map.Count.ShouldBe(2);
        map[a].ShouldNotBe(a);
        map[b].ShouldNotBe(b);
        map[a].ShouldNotBe(map[b]);
    }

    [Fact]
    public void A_copied_subtask_points_at_its_copied_parent()
    {
        var parent = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        var map = TaskDuplicationMapping.BuildIdMap([parent, child], Guid.CreateVersion7);

        TaskDuplicationMapping.RemapParent(parent, map).ShouldBe(map[parent]);
    }

    [Fact]
    public void A_parent_outside_the_copied_set_is_dropped_not_left_dangling()
    {
        var copiedChild = Guid.CreateVersion7();
        var uncopiedParent = Guid.CreateVersion7();
        var map = TaskDuplicationMapping.BuildIdMap([copiedChild], Guid.CreateVersion7);

        TaskDuplicationMapping.RemapParent(uncopiedParent, map).ShouldBeNull();
    }

    [Fact]
    public void A_root_level_task_stays_rootless()
    {
        var map = TaskDuplicationMapping.BuildIdMap([Guid.CreateVersion7()], Guid.CreateVersion7);
        TaskDuplicationMapping.RemapParent(null, map).ShouldBeNull();
    }
}

/// <summary>Custom-field resolution, including Folder-scoped inheritance down to nested Lists.</summary>
public sealed class CustomFieldResolutionTests
{
    private static CustomFieldDefinition Field(CustomFieldScope scope, Guid? scopeId, string name)
        => CustomFieldDefinition.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), scope, scopeId, name, CustomFieldType.Text, isRequired: false, position: 0);

    [Fact]
    public void Workspace_scoped_fields_apply_everywhere()
    {
        var workspaceField = Field(CustomFieldScope.Workspace, null, "Priority tag");
        var result = CustomFieldResolution.EffectiveForList([workspaceField], Guid.CreateVersion7(), [], Guid.CreateVersion7());

        result.ShouldContain(workspaceField);
    }

    [Fact]
    public void Space_scoped_field_only_applies_to_lists_in_that_space()
    {
        var spaceId = Guid.CreateVersion7();
        var otherSpaceId = Guid.CreateVersion7();
        var spaceField = Field(CustomFieldScope.Space, spaceId, "Region");

        CustomFieldResolution.EffectiveForList([spaceField], spaceId, [], Guid.CreateVersion7()).ShouldContain(spaceField);
        CustomFieldResolution.EffectiveForList([spaceField], otherSpaceId, [], Guid.CreateVersion7()).ShouldNotContain(spaceField);
    }

    [Fact]
    public void Folder_scoped_field_is_inherited_by_a_list_nested_under_that_folder()
    {
        var folderId = Guid.CreateVersion7();
        var listId = Guid.CreateVersion7();
        var folderField = Field(CustomFieldScope.Folder, folderId, "Budget code");

        // The list sits directly under the folder: its ancestor-folder-id chain is just [folderId].
        var result = CustomFieldResolution.EffectiveForList([folderField], Guid.CreateVersion7(), [folderId], listId);

        result.ShouldContain(folderField);
    }

    [Fact]
    public void Folder_scoped_field_is_inherited_through_nested_subfolders()
    {
        var grandparentFolderId = Guid.CreateVersion7();
        var parentFolderId = Guid.CreateVersion7();
        var listId = Guid.CreateVersion7();
        var fieldOnGrandparent = Field(CustomFieldScope.Folder, grandparentFolderId, "Cost centre");

        // List -> parent folder -> grandparent folder: ancestor chain has both folder ids.
        var result = CustomFieldResolution.EffectiveForList(
            [fieldOnGrandparent], Guid.CreateVersion7(), [parentFolderId, grandparentFolderId], listId);

        result.ShouldContain(fieldOnGrandparent);
    }

    [Fact]
    public void Folder_scoped_field_does_not_leak_to_a_list_outside_that_folder_tree()
    {
        var folderId = Guid.CreateVersion7();
        var unrelatedFolderId = Guid.CreateVersion7();
        var folderField = Field(CustomFieldScope.Folder, folderId, "Budget code");

        var result = CustomFieldResolution.EffectiveForList([folderField], Guid.CreateVersion7(), [unrelatedFolderId], Guid.CreateVersion7());

        result.ShouldNotContain(folderField);
    }

    [Fact]
    public void List_scoped_field_only_applies_to_that_exact_list()
    {
        var listId = Guid.CreateVersion7();
        var listField = Field(CustomFieldScope.List, listId, "Sign-off");

        CustomFieldResolution.EffectiveForList([listField], Guid.CreateVersion7(), [], listId).ShouldContain(listField);
        CustomFieldResolution.EffectiveForList([listField], Guid.CreateVersion7(), [], Guid.CreateVersion7()).ShouldNotContain(listField);
    }

    [Fact]
    public void All_scopes_combine_for_the_full_effective_set()
    {
        var spaceId = Guid.CreateVersion7();
        var folderId = Guid.CreateVersion7();
        var listId = Guid.CreateVersion7();

        var workspaceField = Field(CustomFieldScope.Workspace, null, "Ws");
        var spaceField = Field(CustomFieldScope.Space, spaceId, "Sp");
        var folderField = Field(CustomFieldScope.Folder, folderId, "Fo");
        var listField = Field(CustomFieldScope.List, listId, "Li");
        var unrelatedField = Field(CustomFieldScope.List, Guid.CreateVersion7(), "Other list");

        var result = CustomFieldResolution.EffectiveForList(
            [workspaceField, spaceField, folderField, listField, unrelatedField], spaceId, [folderId], listId);

        result.ShouldBe([workspaceField, spaceField, folderField, listField], ignoreOrder: true);
    }
}
