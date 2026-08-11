import type { QueryClient } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-client";

const createTaskMock = vi.fn();
const updateTaskMock = vi.fn();
const getTaskMock = vi.fn();
const addCommentMock = vi.fn();
const startTimerMock = vi.fn();
const stopTimerMock = vi.fn();

vi.mock("@/lib/work/client", () => ({
  createTask: (...args: unknown[]) => createTaskMock(...args),
  updateTask: (...args: unknown[]) => updateTaskMock(...args),
  getTask: (...args: unknown[]) => getTaskMock(...args),
}));
vi.mock("@/lib/collab/client", () => ({
  addComment: (...args: unknown[]) => addCommentMock(...args),
}));
vi.mock("@/lib/time/client", () => ({
  startTimer: (...args: unknown[]) => startTimerMock(...args),
  stopTimer: (...args: unknown[]) => stopTimerMock(...args),
}));

const { replayOutbox } = await import("./replay");
const { markOnline, isOnline } = await import("./connectivity");
const { __resetForTests, outboxAdd, outboxListAll, conflictListByWorkspace } = await import("./db");

function fakeQueryClient() {
  return { invalidateQueries: vi.fn() } as unknown as QueryClient;
}

describe("replayOutbox", () => {
  beforeEach(async () => {
    await __resetForTests();
    createTaskMock.mockReset();
    updateTaskMock.mockReset();
    getTaskMock.mockReset();
    addCommentMock.mockReset();
    startTimerMock.mockReset();
    stopTimerMock.mockReset();
    markOnline();
  });

  afterEach(async () => {
    await __resetForTests();
  });

  it("replays in order and remaps a comment queued against an offline-created task's temp id to the real server id", async () => {
    const tempTaskId = "temp-task-1";
    createTaskMock.mockResolvedValue({ id: "real-task-42", title: "Ship it" });
    addCommentMock.mockResolvedValue({ id: "comment-1" });

    await outboxAdd({
      id: tempTaskId,
      workspaceId: "workspace-a",
      type: "task.create",
      payload: { listId: "list-1", title: "Ship it" },
      createdAtUtc: "2026-01-01T00:00:00.000Z",
      status: "pending",
    });
    await outboxAdd({
      id: "comment-outbox-1",
      workspaceId: "workspace-a",
      type: "comment.create",
      payload: { taskId: tempTaskId, body: "Nice work" },
      createdAtUtc: "2026-01-01T00:00:01.000Z",
      status: "pending",
    });

    const result = await replayOutbox(fakeQueryClient());

    expect(result).toEqual({ synced: 2, failed: 0 });
    expect(addCommentMock).toHaveBeenCalledWith(
      expect.objectContaining({ taskId: "real-task-42", body: "Nice work" }),
      expect.anything(),
    );
    expect(await outboxListAll()).toHaveLength(0);
  });

  it("stops the batch and keeps the item pending when a network error occurs (goes back offline mid-replay)", async () => {
    createTaskMock.mockRejectedValue(new TypeError("Failed to fetch"));

    await outboxAdd({
      id: "task-1",
      workspaceId: "workspace-a",
      type: "task.create",
      payload: { listId: "list-1", title: "Ship it" },
      createdAtUtc: "2026-01-01T00:00:00.000Z",
      status: "pending",
    });

    const result = await replayOutbox(fakeQueryClient());

    expect(result).toEqual({ synced: 0, failed: 0 });
    expect(isOnline()).toBe(false);
    const [remaining] = await outboxListAll();
    expect(remaining.status).toBe("pending");

    markOnline(); // restore for subsequent tests
  });

  it("marks a genuine (non-network) API rejection as an error instead of retrying it forever", async () => {
    createTaskMock.mockRejectedValue(new ApiError("Validation failed", 422, {}, null));

    await outboxAdd({
      id: "task-1",
      workspaceId: "workspace-a",
      type: "task.create",
      payload: { listId: "list-1", title: "" },
      createdAtUtc: "2026-01-01T00:00:00.000Z",
      status: "pending",
    });

    const result = await replayOutbox(fakeQueryClient());

    expect(result).toEqual({ synced: 0, failed: 1 });
    const [remaining] = await outboxListAll();
    expect(remaining.status).toBe("error");
  });

  it("records a conflict warning when a queued task.update's untouched fields diverged server-side, but still applies the patch (last-write-wins)", async () => {
    getTaskMock.mockResolvedValue({ id: "task-1", title: "Ship it", statusId: "in-progress" });
    updateTaskMock.mockResolvedValue({ id: "task-1", title: "Ship it (edited)", statusId: "in-progress" });

    await outboxAdd({
      id: "update-1",
      workspaceId: "workspace-a",
      type: "task.update",
      payload: { taskId: "task-1", patch: { title: "Ship it (edited)" } },
      baseSnapshot: { title: "Ship it", statusId: "todo" },
      createdAtUtc: "2026-01-01T00:00:00.000Z",
      status: "pending",
    });

    const result = await replayOutbox(fakeQueryClient());

    expect(result).toEqual({ synced: 1, failed: 0 });
    expect(updateTaskMock).toHaveBeenCalledWith("task-1", { title: "Ship it (edited)" }, expect.anything());
    const conflicts = await conflictListByWorkspace("workspace-a");
    expect(conflicts).toHaveLength(1);
    expect(conflicts[0].fields).toEqual(["statusId"]);
  });
});
