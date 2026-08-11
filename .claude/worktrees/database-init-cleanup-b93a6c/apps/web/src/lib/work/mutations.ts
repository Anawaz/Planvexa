"use client";

import {
  useMutation,
  useQueryClient,
  type QueryClient,
  type QueryKey,
} from "@tanstack/react-query";
import { useMemo } from "react";
import {
  completeTask as completeTaskRequest,
  moveTask as moveTaskRequest,
  reopenTask as reopenTaskRequest,
} from "./client";
import { updateTaskOffline } from "./offlineMutations";
import { workKeys } from "./queries";
import type {
  MoveTaskInput,
  StatusDefinition,
  Task,
  TaskDetail,
  UpdateTaskPatch,
} from "./types";

type TaskSnapshot = {
  taskQueries: Array<[QueryKey, Task[] | undefined]>;
  myTasks: Task[] | undefined;
  taskDetail: TaskDetail | undefined;
};

function closesTask(status: StatusDefinition) {
  return status.category === "Done" || status.category === "Closed";
}

function snapshotTaskState(queryClient: QueryClient, taskId: string): TaskSnapshot {
  return {
    taskQueries: queryClient.getQueriesData<Task[]>({
      queryKey: workKeys.tasksAll(),
    }),
    myTasks: queryClient.getQueryData<Task[]>(workKeys.myTasks()),
    taskDetail: queryClient.getQueryData<TaskDetail>(workKeys.task(taskId)),
  };
}

function restoreTaskState(queryClient: QueryClient, snapshot: TaskSnapshot) {
  snapshot.taskQueries.forEach(([queryKey, data]) => {
    queryClient.setQueryData(queryKey, data);
  });
  queryClient.setQueryData(workKeys.myTasks(), snapshot.myTasks);

  if (snapshot.taskDetail) {
    queryClient.setQueryData(workKeys.task(snapshot.taskDetail.id), snapshot.taskDetail);
  }
}

/** The task as currently cached (any of the query roots that might hold it), used as the offline
 * conflict-detection baseline — see `offlineMutations.ts`'s `updateTaskOffline` and `offline/conflict.ts`. */
function findCachedTask(queryClient: QueryClient, taskId: string): Task | undefined {
  const fromDetail = queryClient.getQueryData<TaskDetail>(workKeys.task(taskId));
  if (fromDetail) return fromDetail;
  const fromMyTasks = queryClient.getQueryData<Task[]>(workKeys.myTasks())?.find((task) => task.id === taskId);
  if (fromMyTasks) return fromMyTasks;
  for (const [, tasks] of queryClient.getQueriesData<Task[]>({ queryKey: workKeys.tasksAll() })) {
    const match = tasks?.find((task) => task.id === taskId);
    if (match) return match;
  }
  return undefined;
}

function updateCachedTask(
  queryClient: QueryClient,
  taskId: string,
  updater: <T extends Task>(task: T) => T,
) {
  queryClient.setQueriesData<Task[]>(
    { queryKey: workKeys.tasksAll() },
    (currentTasks) =>
      currentTasks?.map((task) => (task.id === taskId ? updater(task) : task)),
  );
  queryClient.setQueryData<Task[]>(workKeys.myTasks(), (currentTasks) =>
    currentTasks?.map((task) => (task.id === taskId ? updater(task) : task)),
  );
  queryClient.setQueryData<TaskDetail>(workKeys.task(taskId), (task) =>
    task ? updater(task) : task,
  );
}

/**
 * Invalidate-on-success mutation for the small task edits (create, assignees, tags, checklists,
 * watchers). No optimistic cache patching — the root-key invalidation is cheap at this scale.
 */
export function useWorkMutation<TVariables, TData>(
  mutationFn: (variables: TVariables) => Promise<TData>,
) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: workKeys.all }),
  });
}

/**
 * One mutation for every structure write (create/rename/archive/delete of a space, folder or
 * list): pass the call as a thunk, the work cache invalidates on success.
 * ponytail: a shared `isPending` disables the whole panel during a write — fine at one
 * click at a time; split into per-operation mutations if concurrent writes ever matter.
 */
export function useWorkAction() {
  return useWorkMutation((run: () => Promise<unknown>) => run());
}

export function useTaskMutations(statuses: StatusDefinition[] = []) {
  const queryClient = useQueryClient();
  const statusById = useMemo(
    () => new Map(statuses.map((status) => [status.id, status])),
    [statuses],
  );
  const doneStatus = statuses.find((status) => status.category === "Done");
  const reopenStatus =
    statuses.find((status) => status.category === "Active") ?? statuses[0];

  const updateTask = useMutation({
    mutationFn: ({ taskId, patch }: { taskId: string; patch: UpdateTaskPatch }) =>
      updateTaskOffline(taskId, patch, findCachedTask(queryClient, taskId)),
    onMutate: async ({ taskId, patch }) => {
      await queryClient.cancelQueries({ queryKey: workKeys.all });
      const snapshot = snapshotTaskState(queryClient, taskId);
      const nextStatus = patch.statusId ? statusById.get(patch.statusId) : undefined;

      updateCachedTask(queryClient, taskId, (task) => ({
        ...task,
        ...patch,
        isCompleted:
          patch.isCompleted ?? (nextStatus ? closesTask(nextStatus) : task.isCompleted),
      }));

      return snapshot;
    },
    onError: (_error, _variables, snapshot) => {
      if (snapshot) {
        restoreTaskState(queryClient, snapshot);
      }
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: workKeys.all });
    },
  });

  const moveTask = useMutation({
    mutationFn: ({ taskId, input }: { taskId: string; input: MoveTaskInput }) =>
      moveTaskRequest(taskId, input),
    onMutate: async ({ taskId, input }) => {
      await queryClient.cancelQueries({ queryKey: workKeys.all });
      const snapshot = snapshotTaskState(queryClient, taskId);
      const nextStatus = input.statusId ? statusById.get(input.statusId) : undefined;

      updateCachedTask(queryClient, taskId, (task) => ({
        ...task,
        listId: input.listId ?? task.listId,
        position: input.position ?? task.position,
        statusId: input.statusId ?? task.statusId,
        isCompleted: nextStatus ? closesTask(nextStatus) : task.isCompleted,
      }));

      return snapshot;
    },
    onError: (_error, _variables, snapshot) => {
      if (snapshot) {
        restoreTaskState(queryClient, snapshot);
      }
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: workKeys.all });
    },
  });

  const completeTask = useMutation({
    mutationFn: (taskId: string) => completeTaskRequest(taskId),
    onMutate: async (taskId) => {
      await queryClient.cancelQueries({ queryKey: workKeys.all });
      const snapshot = snapshotTaskState(queryClient, taskId);

      updateCachedTask(queryClient, taskId, (task) => ({
        ...task,
        statusId: doneStatus?.id ?? task.statusId,
        isCompleted: true,
      }));

      return snapshot;
    },
    onError: (_error, _variables, snapshot) => {
      if (snapshot) {
        restoreTaskState(queryClient, snapshot);
      }
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: workKeys.all });
    },
  });

  const reopenTask = useMutation({
    mutationFn: (taskId: string) => reopenTaskRequest(taskId),
    onMutate: async (taskId) => {
      await queryClient.cancelQueries({ queryKey: workKeys.all });
      const snapshot = snapshotTaskState(queryClient, taskId);

      updateCachedTask(queryClient, taskId, (task) => ({
        ...task,
        statusId: reopenStatus?.id ?? task.statusId,
        isCompleted: false,
      }));

      return snapshot;
    },
    onError: (_error, _variables, snapshot) => {
      if (snapshot) {
        restoreTaskState(queryClient, snapshot);
      }
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: workKeys.all });
    },
  });

  return {
    updateTask,
    moveTask,
    completeTask,
    reopenTask,
  };
}
