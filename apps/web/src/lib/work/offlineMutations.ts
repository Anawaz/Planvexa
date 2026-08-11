/**
 * Drop-in, offline-aware replacements for `createTask`/`updateTask`. Same call
 * signature as the originals so existing `useMutation({ mutationFn: createTask })` call sites (
 * BoardView, ListView, QuickAddTask, TaskDetailPanel) only need their import swapped.
 *
 * When online, delegates straight through. When offline (or the live call fails with a network
 * error), queues the mutation in the IndexedDB outbox and returns a locally-synthesized task so the
 * calling `useMutation`'s existing invalidate-on-success/optimistic-update logic still has something
 * to render — `listTasks`'s offline read-through cache (see work/client.ts) is what actually makes it
 * show up after the resulting `invalidateQueries()` call, since there is no live refetch while offline.
 */
import { getApiContext } from "@/lib/api-client";
import { cachePut } from "@/lib/offline/db";
import { queueOrRun } from "@/lib/offline/withOfflineFallback";
import { createTask as createTaskRequest, updateTask as updateTaskRequest } from "./client";
import type { CreateTaskInput, Task, UpdateTaskPatch } from "./types";

function buildOptimisticTask(localId: string, input: CreateTaskInput): Task {
  return {
    id: localId,
    listId: input.listId,
    // ponytail: spaceId is only known server-side (resolved from the list); left blank until sync
    // replaces this placeholder via the post-replay `invalidateQueries()` refetch. Space-scoped views
    // (rather than the list-scoped Board/List/QuickAdd views this feeds) may not show an offline-
    // created task until it syncs -- acceptable gap here.
    spaceId: "",
    parentId: input.parentId,
    sequence: "-",
    title: input.title,
    description: input.description,
    statusId: input.statusId ?? "",
    priority: input.priority ?? "None",
    startDate: input.startDate,
    dueDate: input.dueDate,
    isMilestone: input.isMilestone ?? false,
    assigneeUserIds: input.assigneeUserIds ?? [],
    watcherUserIds: input.watcherUserIds ?? [],
    tagIds: input.tagIds ?? [],
    position: Number.MAX_SAFE_INTEGER,
    isCompleted: false,
    isPrivate: false,
    taskTypeId: input.taskTypeId,
    customId: input.customId,
    teamAssigneeIds: [],
    isArchived: false,
  };
}

export async function createTaskOffline(input: CreateTaskInput): Promise<Task> {
  const workspaceId = getApiContext().workspaceId;
  const { result, queued, localId } = await queueOrRun<Task>({
    workspaceId,
    type: "task.create",
    payload: input as unknown as Record<string, unknown>,
    onlineCall: (idempotencyKey) => createTaskRequest(input, { idempotencyKey }),
    buildOptimistic: (id) => buildOptimisticTask(id, input),
  });

  if (queued && workspaceId) {
    // So the offline read-through fallback in `listTasks` includes it immediately.
    void cachePut(workspaceId, "task", localId, result);
  }

  return result;
}

export async function updateTaskOffline(taskId: string, patch: UpdateTaskPatch, baseTask: Task | undefined): Promise<Task> {
  const workspaceId = getApiContext().workspaceId;
  const { result, queued } = await queueOrRun<Task>({
    workspaceId,
    type: "task.update",
    payload: { taskId, patch } as unknown as Record<string, unknown>,
    baseSnapshot: baseTask as unknown as Record<string, unknown> | undefined,
    onlineCall: () => updateTaskRequest(taskId, patch),
    buildOptimistic: () => ({ ...(baseTask as Task), ...patch, id: taskId }) as Task,
  });

  if (queued && workspaceId) {
    void cachePut(workspaceId, "task", taskId, result);
  }

  return result;
}
