/** Offline-aware drop-in replacements for `startTimer`/`stopTimer` — same shape as
 * `work/offlineMutations.ts`'s `createTaskOffline`; see its doc comment for the general pattern. */
import { getApiContext } from "@/lib/api-client";
import { queueOrRun } from "@/lib/offline/withOfflineFallback";
import { startTimer as startTimerRequest, stopTimer as stopTimerRequest } from "./client";
import type { ActiveTimer, StartTimerInput, TimeEntry } from "./types";

export async function startTimerOffline(input: StartTimerInput, currentUserId: string): Promise<ActiveTimer> {
  const workspaceId = getApiContext().workspaceId;
  const { result } = await queueOrRun<ActiveTimer>({
    workspaceId,
    type: "timeEntry.start",
    payload: input as unknown as Record<string, unknown>,
    onlineCall: (idempotencyKey) => startTimerRequest(input, { idempotencyKey }),
    buildOptimistic: (localId) => ({
      id: localId,
      userId: currentUserId,
      taskId: input.taskId ?? null,
      startedAtUtc: new Date().toISOString(),
      endedAtUtc: null,
      durationSeconds: 0,
      timeZoneId: "UTC",
      description: input.description,
      isBillable: input.isBillable ?? false,
      billingRate: 0,
      costRate: 0,
      source: "Timer",
      approvalStatus: "Draft",
      tags: [],
      isPaused: false,
      pausedAtUtc: null,
      pausedSeconds: 0,
    }),
  });
  return result;
}

export async function stopTimerOffline(activeTimer: ActiveTimer | null | undefined, description?: string): Promise<TimeEntry> {
  const workspaceId = getApiContext().workspaceId;
  const { result } = await queueOrRun<TimeEntry>({
    workspaceId,
    type: "timeEntry.stop",
    payload: { description },
    onlineCall: (idempotencyKey) => {
      void idempotencyKey; // stop has no server-side idempotency support -- see replay.ts's doc comment
      return stopTimerRequest(description);
    },
    buildOptimistic: () => ({
      ...(activeTimer as TimeEntry),
      endedAtUtc: new Date().toISOString(),
      durationSeconds: activeTimer ? Math.max(0, Math.floor((Date.now() - new Date(activeTimer.startedAtUtc).getTime()) / 1000)) : 0,
      description: description ?? activeTimer?.description,
    }),
  });
  return result;
}
