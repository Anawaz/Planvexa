import { describe, expect, it } from "vitest";
import { searchResultHref } from "./client";

describe("searchResultHref", () => {
  it("deep-links a task so its drawer opens on arrival", () => {
    expect(
      searchResultHref({ type: "Task", id: "t1", title: "Ship it", listId: "l1" }),
    ).toBe("/app/lists/l1?task=t1");
  });

  it("sends a list to its own page without a task param", () => {
    expect(searchResultHref({ type: "List", id: "l1", title: "Backlog", listId: "l1" })).toBe(
      "/app/lists/l1",
    );
  });

  it("sends spaces, and anything without a list, to the spaces page", () => {
    expect(searchResultHref({ type: "Space", id: "s1", title: "Engineering" })).toBe("/app/spaces");
    expect(searchResultHref({ type: "Task", id: "t1", title: "Orphan", listId: null })).toBe(
      "/app/spaces",
    );
  });
});
