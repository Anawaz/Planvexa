import { describe, expect, it } from "vitest";
import { detectConflict } from "./conflict";

describe("detectConflict", () => {
  it("flags no conflict when the server matches what the client last knew", () => {
    const base = { title: "Ship it", statusId: "todo", priority: "Normal" };
    const server = { title: "Ship it", statusId: "todo", priority: "Normal" };

    const result = detectConflict(base, server, ["priority"]);

    expect(result.hasConflict).toBe(false);
    expect(result.fields).toEqual([]);
  });

  it("flags a conflict when a field the offline patch does NOT touch changed server-side", () => {
    const base = { title: "Ship it", statusId: "todo", priority: "Normal" };
    // Someone else moved it to "in-progress" while this client was offline.
    const server = { title: "Ship it", statusId: "in-progress", priority: "Normal" };

    // The offline client only patched `priority` -- statusId is an untouched field that diverged.
    const result = detectConflict(base, server, ["priority"]);

    expect(result.hasConflict).toBe(true);
    expect(result.fields).toEqual(["statusId"]);
    expect(result.message).toContain("statusId");
  });

  it("excludes fields the queued patch itself is about to overwrite, even if they also drifted server-side", () => {
    const base = { title: "Ship it", statusId: "todo" };
    const server = { title: "Ship it", statusId: "in-progress" };

    // The client's own queued patch touches statusId -- its own edit intentionally wins there.
    const result = detectConflict(base, server, ["statusId"]);

    expect(result.hasConflict).toBe(false);
  });

  it("reports every diverged comparable field, not just the first", () => {
    const base = { title: "Ship it", dueDate: "2026-01-01", description: "old" };
    const server = { title: "Ship it later", dueDate: "2026-02-01", description: "old" };

    const result = detectConflict(base, server, []);

    expect(result.fields.sort()).toEqual(["dueDate", "title"]);
  });
});
