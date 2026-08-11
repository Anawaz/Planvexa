import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  __resetForTests,
  cacheGetAll,
  cachePut,
  cachePutMany,
  clearCacheForWorkspace,
  outboxAdd,
  outboxListAll,
  outboxListByWorkspace,
  outboxRemove,
  outboxUpdate,
  type OutboxItem,
} from "./db";

function makeOutboxItem(overrides: Partial<OutboxItem> = {}): OutboxItem {
  return {
    id: crypto.randomUUID(),
    workspaceId: "workspace-a",
    type: "task.create",
    payload: { title: "Do the thing" },
    createdAtUtc: new Date().toISOString(),
    status: "pending",
    ...overrides,
  };
}

describe("offline db (IndexedDB via fake-indexeddb)", () => {
  beforeEach(async () => {
    await __resetForTests();
  });

  afterEach(async () => {
    await __resetForTests();
  });

  it("scopes the read-through cache by workspace and never returns another workspace's rows", async () => {
    await cachePutMany("workspace-a", "task", [
      { id: "task-1", data: { id: "task-1", title: "A's task" } },
    ]);
    await cachePutMany("workspace-b", "task", [
      { id: "task-2", data: { id: "task-2", title: "B's task" } },
    ]);

    const aTasks = await cacheGetAll("workspace-a", "task");
    const bTasks = await cacheGetAll("workspace-b", "task");

    expect(aTasks.map((entry) => entry.id)).toEqual(["task-1"]);
    expect(bTasks.map((entry) => entry.id)).toEqual(["task-2"]);
  });

  it("CRITICAL: clearing the outgoing workspace's cache on switch never leaves the new workspace's data affected, and the old workspace's data is gone", async () => {
    await cachePut("workspace-a", "task", "task-1", { id: "task-1", title: "A's task" });
    await cachePut("workspace-a", "comment", "comment-1", { id: "comment-1", body: "A's comment" });
    await cachePut("workspace-b", "task", "task-2", { id: "task-2", title: "B's task" });

    // Simulates AppContext.tsx's setCurrentWorkspaceId switching away from workspace-a.
    await clearCacheForWorkspace("workspace-a");

    const aTasksAfter = await cacheGetAll("workspace-a", "task");
    const aCommentsAfter = await cacheGetAll("workspace-a", "comment");
    const bTasksAfter = await cacheGetAll("workspace-b", "task");

    expect(aTasksAfter).toHaveLength(0);
    expect(aCommentsAfter).toHaveLength(0);
    // The other (now-active) workspace's cache must be completely untouched.
    expect(bTasksAfter.map((entry) => entry.id)).toEqual(["task-2"]);
  });

  it("does NOT clear the outbox on a workspace switch -- queued offline edits must still sync later", async () => {
    const item = makeOutboxItem({ workspaceId: "workspace-a" });
    await outboxAdd(item);

    await clearCacheForWorkspace("workspace-a");

    const remaining = await outboxListByWorkspace("workspace-a");
    expect(remaining.map((entry) => entry.id)).toEqual([item.id]);
  });

  it("outbox items round-trip across workspaces and outboxListAll returns oldest first", async () => {
    const first = makeOutboxItem({ workspaceId: "workspace-a", createdAtUtc: "2026-01-01T00:00:00.000Z" });
    const second = makeOutboxItem({ workspaceId: "workspace-b", createdAtUtc: "2026-01-02T00:00:00.000Z" });
    await outboxAdd(second);
    await outboxAdd(first);

    const all = await outboxListAll();
    expect(all.map((entry) => entry.id)).toEqual([first.id, second.id]);
  });

  it("outboxUpdate patches in place and outboxRemove deletes", async () => {
    const item = makeOutboxItem();
    await outboxAdd(item);

    await outboxUpdate(item.id, { status: "error", error: "boom" });
    const [updated] = await outboxListByWorkspace(item.workspaceId);
    expect(updated.status).toBe("error");
    expect(updated.error).toBe("boom");

    await outboxRemove(item.id);
    const remaining = await outboxListByWorkspace(item.workspaceId);
    expect(remaining).toHaveLength(0);
  });
});
