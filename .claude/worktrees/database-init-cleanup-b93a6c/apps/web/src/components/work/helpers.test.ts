import { describe, expect, it } from "vitest";
import type { CustomFieldDefinition, CustomFieldType, Task } from "@/lib/work/types";
import { buildTaskTree, customFieldEditor, customFieldInputValue, shortId } from "./helpers";

function task(id: string, parentId?: string): Task {
  return {
    id,
    listId: "list",
    spaceId: "space",
    parentId,
    sequence: id,
    title: id,
    statusId: "status",
    priority: "Normal",
    isMilestone: false,
    assigneeUserIds: [],
    watcherUserIds: [],
    tagIds: [],
    position: 0,
    isCompleted: false,
    isPrivate: false,
    teamAssigneeIds: [],
  };
}

function definition(type: CustomFieldType): CustomFieldDefinition {
  return {
    id: "def",
    name: type,
    type,
    scope: "Workspace",
    isRequired: false,
    position: 0,
    options: [],
  };
}

describe("buildTaskTree", () => {
  it("keeps parents as roots and indexes their children", () => {
    const tree = buildTaskTree([task("a"), task("a1", "a"), task("b")]);

    expect(tree.roots.map((entry) => entry.id)).toEqual(["a", "b"]);
    expect(tree.childrenOf.get("a")?.map((entry) => entry.id)).toEqual(["a1"]);
  });

  it("promotes an orphan whose parent is filtered out", () => {
    const tree = buildTaskTree([task("a1", "missing")]);

    expect(tree.roots.map((entry) => entry.id)).toEqual(["a1"]);
    expect(tree.childrenOf.size).toBe(0);
  });

  it("returns empty structures for no tasks", () => {
    expect(buildTaskTree([])).toEqual({ roots: [], childrenOf: new Map() });
  });
});

describe("shortId", () => {
  it("keeps the first eight characters", () => {
    expect(shortId("018f0000-0000-7000-8000-000000012001")).toBe("#018f0000");
  });
});

describe("customFieldEditor", () => {
  it("maps every type to a control", () => {
    expect(customFieldEditor("LongText")).toBe("text");
    expect(customFieldEditor("Currency")).toBe("number");
    expect(customFieldEditor("DateTime")).toBe("date");
    expect(customFieldEditor("Boolean")).toBe("boolean");
    expect(customFieldEditor("Dropdown")).toBe("dropdown");
    expect(customFieldEditor("MultiSelect")).toBe("readonly");
  });

  it("maps the custom field types", () => {
    expect(customFieldEditor("Phone")).toBe("text");
    expect(customFieldEditor("Location")).toBe("text");
    expect(customFieldEditor("Progress")).toBe("number");
    expect(customFieldEditor("User")).toBe("user");
    expect(customFieldEditor("Team")).toBe("team");
    expect(customFieldEditor("Relationship")).toBe("relationship");
    expect(customFieldEditor("Formula")).toBe("computed");
    expect(customFieldEditor("Rollup")).toBe("computed");
  });
});

describe("customFieldInputValue", () => {
  it("reads the slot that matches the definition type", () => {
    expect(customFieldInputValue(definition("Text"), { definitionId: "def", text: "hi" })).toBe("hi");
    expect(customFieldInputValue(definition("Number"), { definitionId: "def", number: 0 })).toBe("0");
    expect(customFieldInputValue(definition("Date"), { definitionId: "def", date: "2026-08-01" })).toBe(
      "2026-08-01",
    );
    expect(customFieldInputValue(definition("Boolean"), { definitionId: "def", boolean: false })).toBe(
      "false",
    );
    expect(customFieldInputValue(definition("Dropdown"), { definitionId: "def", optionId: "opt" })).toBe(
      "opt",
    );
  });

  it("is empty when the task has no value", () => {
    expect(customFieldInputValue(definition("Text"), undefined)).toBe("");
    expect(customFieldInputValue(definition("Number"), { definitionId: "def" })).toBe("");
  });

  it("reads User/Team ids and surfaces a computed field's error over its value", () => {
    expect(customFieldInputValue(definition("User"), { definitionId: "def", userValue: "user-1" })).toBe(
      "user-1",
    );
    expect(customFieldInputValue(definition("Team"), { definitionId: "def", teamValue: "team-1" })).toBe(
      "team-1",
    );
    expect(customFieldInputValue(definition("Formula"), { definitionId: "def", number: 7 })).toBe("7");
    expect(
      customFieldInputValue(definition("Formula"), { definitionId: "def", computedError: "Division by zero." }),
    ).toBe("Division by zero.");
  });
});
