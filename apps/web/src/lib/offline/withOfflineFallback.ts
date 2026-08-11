import { ApiError } from "@/lib/api-client";
import { isOnline, markOffline } from "./connectivity";
import { outboxAdd, type OutboxItem, type OutboxMutationType } from "./db";
import { requestBackgroundSync } from "./registerServiceWorker";

/**
 * `fetch()` throws a bare `TypeError` ("Failed to fetch" / "Load failed") when the network is
 * unreachable — a genuinely different failure mode from `ApiError`, which means the server was
 * reached and rejected the request (validation, auth, conflict, ...). Only the former is worth
 * queueing for retry; retrying a rejected request would just fail the same way again.
 */
function isNetworkError(error: unknown): boolean {
  return !(error instanceof ApiError) && error instanceof TypeError;
}

/**
 * Runs `onlineCall` when connectivity looks good; falls back to queueing the mutation in the
 * IndexedDB outbox (for replay on reconnect — see replay.ts) when offline or when the call itself
 * fails with a network error. Either way the caller gets back a value synchronously-ish: the real
 * server result when it succeeded live, or an optimistic locally-built one when queued.
 */
export async function queueOrRun<T>(options: {
  workspaceId: string | undefined;
  type: OutboxMutationType;
  payload: Record<string, unknown>;
  baseSnapshot?: Record<string, unknown>;
  onlineCall: (idempotencyKey: string) => Promise<T>;
  buildOptimistic: (localId: string) => T;
}): Promise<{ result: T; queued: boolean; localId: string }> {
  const { workspaceId, type, payload, baseSnapshot, onlineCall, buildOptimistic } = options;
  if (!workspaceId) {
    throw new Error("Cannot queue an offline mutation without an active workspace.");
  }

  const idempotencyKey = crypto.randomUUID();

  if (isOnline()) {
    try {
      const result = await onlineCall(idempotencyKey);
      return { result, queued: false, localId: idempotencyKey };
    } catch (error) {
      if (!isNetworkError(error)) {
        throw error;
      }
      markOffline();
      // fall through to queue below
    }
  }

  const item: OutboxItem = {
    id: idempotencyKey,
    workspaceId,
    type,
    payload,
    baseSnapshot,
    createdAtUtc: new Date().toISOString(),
    status: "pending",
  };
  await outboxAdd(item);
  void requestBackgroundSync();
  return { result: buildOptimistic(idempotencyKey), queued: true, localId: idempotencyKey };
}

export { isNetworkError };
