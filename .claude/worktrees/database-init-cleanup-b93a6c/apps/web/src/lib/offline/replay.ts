/**
 * Replays the IndexedDB outbox against the real API on reconnect.
 *
 * Trigger mechanism: the browser `online` event + the realtime layer's `onreconnected` signal (see
 * `useOfflineSync.ts`), NOT the Background Sync API. `ServiceWorkerRegistration.sync` only fires while
 * the service worker is registered and the browser decides to honor it (Chromium-only, no Safari/
 * Firefox support, and it hands control to the SW with no access to this module's IndexedDB helpers or
 * react-query cache without a postMessage round-trip). For this scope — a same-tab web app, not an
 * installed app expected to sync while fully closed — "replay on app-foreground + online event" is
 * simpler, universally supported, and sufficient; the SW's own `sync` handler (sw.js) additionally
 * calls `postMessage({type:"sync-outbox"})` to any open client as a best-effort nudge if it does fire.
 *
 * Replays in creation order (oldest first) so a comment queued against a task that was ALSO created
 * offline in the same session resolves the task's real, server-assigned id (via `idRemap`) before the
 * comment is sent — outbox items reference an offline-created task by the create-item's `id` (its
 * idempotency key) until the create syncs.
 */
import type { QueryClient } from "@tanstack/react-query";
import { ApiError } from "@/lib/api-client";
import { addComment } from "@/lib/collab/client";
import type { AddCommentInput } from "@/lib/collab/types";
import { startTimer, stopTimer } from "@/lib/time/client";
import type { StartTimerInput } from "@/lib/time/types";
import { createTask, getTask, updateTask } from "@/lib/work/client";
import type { CreateTaskInput, UpdateTaskPatch } from "@/lib/work/types";
import { detectConflict } from "./conflict";
import { isOnline, markOffline } from "./connectivity";
import { conflictAdd, outboxListAll, outboxRemove, outboxUpdate, type OutboxItem } from "./db";
import { isNetworkError } from "./withOfflineFallback";

/** Rewrites any string field in `value` that is a key in `idRemap` (a temp/offline id) to the real
 * server id it was resolved to earlier in this same replay batch. */
function remap(value: unknown, idRemap: ReadonlyMap<string, string>): unknown {
  if (typeof value === "string") return idRemap.get(value) ?? value;
  if (Array.isArray(value)) return value.map((entry) => remap(entry, idRemap));
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, entry]) => [key, remap(entry, idRemap)]));
  }
  return value;
}

async function replayOne(item: OutboxItem, idRemap: Map<string, string>) {
  const payload = remap(item.payload, idRemap) as Record<string, unknown>;
  const forWorkspace = { workspaceId: item.workspaceId, idempotencyKey: item.id };

  switch (item.type) {
    case "task.create": {
      const created = await createTask(payload as unknown as CreateTaskInput, forWorkspace);
      idRemap.set(item.id, created.id);
      return;
    }
    case "task.update": {
      const { taskId, patch } = payload as unknown as { taskId: string; patch: UpdateTaskPatch };
      if (item.baseSnapshot) {
        // PATCH is naturally idempotent (re-applying the same patch converges to the same state), so
        // no idempotency key is needed here — only a conflict check against what changed meanwhile.
        const current = await getTask(taskId, { workspaceId: item.workspaceId });
        const conflict = detectConflict(item.baseSnapshot, current as unknown as Record<string, unknown>, Object.keys(patch));
        if (conflict.hasConflict) {
          await conflictAdd({
            id: crypto.randomUUID(),
            workspaceId: item.workspaceId,
            taskId,
            message: conflict.message,
            fields: conflict.fields,
            createdAtUtc: new Date().toISOString(),
          });
        }
      }
      await updateTask(taskId, patch, { workspaceId: item.workspaceId });
      return;
    }
    case "comment.create": {
      await addComment(payload as unknown as AddCommentInput, forWorkspace);
      return;
    }
    case "timeEntry.start": {
      await startTimer(payload as unknown as StartTimerInput, forWorkspace);
      return;
    }
    case "timeEntry.stop": {
      try {
        await stopTimer((payload as { description?: string }).description, { workspaceId: item.workspaceId });
      } catch (error) {
        // A stop that already landed server-side (the original request succeeded but its response
        // never reached this client) surfaces as "no running timer" on replay — that is success, not
        // failure; there is no idempotency-key support for this endpoint (it mutates existing state
        // rather than creating a row, so double-application isn't a duplication risk — see
        // WithOfflineFallback's doc comment), so this status-code check is the practical equivalent.
        if (error instanceof ApiError && error.status === 404) return;
        throw error;
      }
      return;
    }
  }
}

export async function replayOutbox(queryClient: QueryClient): Promise<{ synced: number; failed: number }> {
  if (!isOnline()) return { synced: 0, failed: 0 };

  const items = await outboxListAll();
  const idRemap = new Map<string, string>();
  let synced = 0;
  let failed = 0;

  for (const item of items) {
    if (item.status !== "pending") continue;

    await outboxUpdate(item.id, { status: "syncing" });
    try {
      await replayOne(item, idRemap);
      await outboxRemove(item.id);
      synced += 1;
    } catch (error) {
      if (isNetworkError(error)) {
        await outboxUpdate(item.id, { status: "pending" });
        markOffline();
        break; // offline again — stop the batch, the online event will retry the rest later
      }

      // ponytail: a hard (non-network) failure is parked as "error" for manual review rather than
      // retried with backoff — add a retry/backoff policy if silent permanent failures are observed.
      await outboxUpdate(item.id, {
        status: "error",
        error: error instanceof ApiError ? error.message : "Sync failed.",
      });
      failed += 1;
    }
  }

  if (synced > 0) {
    // Same "just reconnected, refetch everything on screen" policy useRealtime.ts already uses on
    // SignalR reconnect — events/writes missed while offline are unrecoverable to replay individually.
    void queryClient.invalidateQueries();
  }

  return { synced, failed };
}
