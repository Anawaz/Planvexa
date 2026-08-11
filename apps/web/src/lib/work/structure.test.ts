import { describe, expect, it } from "vitest";
import { groupListsByFolder, parseExpanded, toggleExpanded } from "./structure";
import type { Folder, TaskList } from "./types";

const folders: Folder[] = [
  { id: "f2", spaceId: "s1", name: "Later", position: 2000, isPrivate: false },
  { id: "f1", spaceId: "s1", name: "Now", position: 1000, isPrivate: false },
];

const lists: TaskList[] = [
  { id: "l3", spaceId: "s1", name: "Loose", position: 3000, isPrivate: false },
  { id: "l1", spaceId: "s1", folderId: "f1", name: "Sprint 2", position: 2000, isPrivate: false },
  { id: "l2", spaceId: "s1", folderId: "f1", name: "Sprint 1", position: 1000, isPrivate: false },
];

describe("parseExpanded", () => {
  it("reads a persisted id list", () => {
    expect(parseExpanded('["a","b"]')).toEqual(["a", "b"]);
  });

  it("drops non-string entries", () => {
    expect(parseExpanded('["a",1,null]')).toEqual(["a"]);
  });

  it("returns nothing for missing or corrupt storage", () => {
    expect(parseExpanded(null)).toEqual([]);
    expect(parseExpanded("not json")).toEqual([]);
    expect(parseExpanded('{"a":1}')).toEqual([]);
  });
});

describe("toggleExpanded", () => {
  it("adds a collapsed id", () => {
    expect(toggleExpanded(["a"], "b")).toEqual(["a", "b"]);
  });

  it("removes an expanded id", () => {
    expect(toggleExpanded(["a", "b"], "a")).toEqual(["b"]);
  });

  it("does not mutate the input", () => {
    const ids = ["a"];
    toggleExpanded(ids, "b");
    expect(ids).toEqual(["a"]);
  });
});

describe("groupListsByFolder", () => {
  it("sorts folders by position and nests their lists", () => {
    const grouped = groupListsByFolder(folders, lists);

    expect(grouped.folders.map((entry) => entry.folder.id)).toEqual(["f1", "f2"]);
    expect(grouped.folders[0].lists.map((list) => list.id)).toEqual(["l2", "l1"]);
    expect(grouped.folders[1].lists).toEqual([]);
  });

  it("keeps folderless lists out of the folder buckets", () => {
    expect(groupListsByFolder(folders, lists).ungrouped.map((list) => list.id)).toEqual(["l3"]);
  });

  it("handles a space with no structure at all", () => {
    expect(groupListsByFolder([], [])).toEqual({ folders: [], ungrouped: [] });
  });

  it("nests subfolders (and their lists) under their parent folder", () => {
    const withSub: Folder[] = [
      { id: "f1", spaceId: "s1", name: "Now", position: 1000, isPrivate: false },
      { id: "sub1", spaceId: "s1", parentFolderId: "f1", name: "Q1", position: 500, isPrivate: false },
    ];
    const withSubLists: TaskList[] = [
      { id: "l1", spaceId: "s1", folderId: "f1", name: "Top list", position: 1000, isPrivate: false },
      { id: "l2", spaceId: "s1", folderId: "sub1", name: "Sub list", position: 1000, isPrivate: false },
    ];

    const grouped = groupListsByFolder(withSub, withSubLists);

    expect(grouped.folders.map((entry) => entry.folder.id)).toEqual(["f1"]);
    expect(grouped.folders[0].lists.map((list) => list.id)).toEqual(["l1"]);
    expect(grouped.folders[0].subfolders.map((entry) => entry.folder.id)).toEqual(["sub1"]);
    expect(grouped.folders[0].subfolders[0].lists.map((list) => list.id)).toEqual(["l2"]);
    expect(grouped.ungrouped).toEqual([]);
  });
});
