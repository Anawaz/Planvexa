/**
 * Workspace-scoped IndexedDB storage for offline reading + the offline mutation outbox.
 *
 * Uses `idb` (ISC-licensed, ~1kb promise wrapper around the native IndexedDB callback API — chosen
 * over raw `indexedDB` because we need versioned schema upgrades + cursor-based "delete everything for
 * workspace X" sweeps, which are painful to hand-roll correctly; still far short of a full client-side
 * database library).
 *
 * CRITICAL: every row in every store carries a `workspaceId` and every read/write here is scoped by it
 * (indexed via `by-workspace`) — this exactly mirrors AppContext.tsx's in-memory react-query cache,
 * which is workspace-scoped and cleared/cancelled on every workspace switch (see
 * `clearCacheForWorkspace` below, wired into AppContext's `setCurrentWorkspaceId`). A previously-active
 * workspace's cached tasks/comments/time-entries must never render after switching to a different one.
 *
 * The `outbox` store is the one exception: queued-but-not-yet-synced mutations survive a workspace
 * switch (switching away from Workspace A must not silently drop A's queued offline edits — they still
 * need to reach the server eventually) and are read back always filtered by the mutation's own
 * `workspaceId`, never the currently-active one.
 */
import { openDB, type DBSchema, type IDBPDatabase } from "idb";

export type CacheEntityKind = "task" | "comment" | "timeEntry";

export type CacheEntry = {
  /** `${workspaceId}::${kind}::${id}` */
  key: string;
  workspaceId: string;
  kind: CacheEntityKind;
  id: string;
  data: unknown;
  cachedAtUtc: string;
};

export type OutboxMutationType = "task.create" | "task.update" | "comment.create" | "timeEntry.start" | "timeEntry.stop";
export type OutboxStatus = "pending" | "syncing" | "error";

export type OutboxItem = {
  /** Also used as the request's Idempotency-Key on every replay attempt (BuildingBlocks-level dedupe
   * on the backend — see AiAssistService/FormSubmission's existing IdempotencyKey pattern), so a
   * retried sync after a partial failure cannot double-create. */
  id: string;
  workspaceId: string;
  type: OutboxMutationType;
  payload: Record<string, unknown>;
  createdAtUtc: string;
  status: OutboxStatus;
  error?: string;
  /** task.update only: the task as last known locally when the edit was queued, used to detect
   * whether someone else changed it server-side while this client was offline (see conflict.ts). */
  baseSnapshot?: Record<string, unknown>;
};

export type ConflictWarning = {
  id: string;
  workspaceId: string;
  taskId: string;
  message: string;
  fields: string[];
  createdAtUtc: string;
};

interface PlanvexaOfflineDb extends DBSchema {
  cache: {
    key: string;
    value: CacheEntry;
    indexes: { "by-workspace": string };
  };
  outbox: {
    key: string;
    value: OutboxItem;
    indexes: { "by-workspace": string; "by-created": string };
  };
  conflicts: {
    key: string;
    value: ConflictWarning;
    indexes: { "by-workspace": string };
  };
}

const DB_NAME = "planvexa-offline";
const DB_VERSION = 1;

let dbPromise: Promise<IDBPDatabase<PlanvexaOfflineDb>> | null = null;

function getDb() {
  if (typeof indexedDB === "undefined") {
    return null;
  }

  dbPromise ??= openDB<PlanvexaOfflineDb>(DB_NAME, DB_VERSION, {
    upgrade(db) {
      const cache = db.createObjectStore("cache", { keyPath: "key" });
      cache.createIndex("by-workspace", "workspaceId");

      const outbox = db.createObjectStore("outbox", { keyPath: "id" });
      outbox.createIndex("by-workspace", "workspaceId");
      outbox.createIndex("by-created", "createdAtUtc");

      const conflicts = db.createObjectStore("conflicts", { keyPath: "id" });
      conflicts.createIndex("by-workspace", "workspaceId");
    },
  });
  return dbPromise;
}

function cacheKey(workspaceId: string, kind: CacheEntityKind, id: string) {
  return `${workspaceId}::${kind}::${id}`;
}

export async function cachePut(workspaceId: string, kind: CacheEntityKind, id: string, data: unknown) {
  const db = await getDb();
  if (!db) return;
  await db.put("cache", { key: cacheKey(workspaceId, kind, id), workspaceId, kind, id, data, cachedAtUtc: new Date().toISOString() });
}

export async function cachePutMany(workspaceId: string, kind: CacheEntityKind, items: Array<{ id: string; data: unknown }>) {
  const db = await getDb();
  if (!db) return;
  const tx = db.transaction("cache", "readwrite");
  const now = new Date().toISOString();
  await Promise.all([
    ...items.map((item) =>
      tx.store.put({ key: cacheKey(workspaceId, kind, item.id), workspaceId, kind, id: item.id, data: item.data, cachedAtUtc: now }),
    ),
    tx.done,
  ]);
}

export async function cacheGetAll(workspaceId: string, kind: CacheEntityKind): Promise<CacheEntry[]> {
  const db = await getDb();
  if (!db) return [];
  const all = await db.getAllFromIndex("cache", "by-workspace", workspaceId);
  return all.filter((entry) => entry.kind === kind);
}

/** Deletes every cached read-through entry for one workspace (task/comment/time-entry snapshots) —
 * called when the active workspace changes, so a stale previous workspace's data cannot bleed through
 * into the newly-selected one. Does NOT touch the outbox: queued offline edits must survive a switch. */
export async function clearCacheForWorkspace(workspaceId: string) {
  const db = await getDb();
  if (!db) return;
  const tx = db.transaction("cache", "readwrite");
  let cursor = await tx.store.index("by-workspace").openCursor(workspaceId);
  while (cursor) {
    await cursor.delete();
    cursor = await cursor.continue();
  }
  await tx.done;
}

// Module-level pub-sub so `useOutboxStatus` can react to outbox writes without polling IndexedDB —
// there is no native "changed" event on the store itself.
const outboxListeners = new Set<() => void>();
function notifyOutboxChanged() {
  for (const listener of outboxListeners) listener();
}
export function subscribeOutboxChanges(listener: () => void) {
  outboxListeners.add(listener);
  return () => outboxListeners.delete(listener);
}

export async function outboxAdd(item: OutboxItem) {
  const db = await getDb();
  if (!db) throw new Error("IndexedDB is not available in this environment.");
  await db.put("outbox", item);
  notifyOutboxChanged();
}

export async function outboxUpdate(id: string, patch: Partial<OutboxItem>) {
  const db = await getDb();
  if (!db) return;
  const existing = await db.get("outbox", id);
  if (!existing) return;
  await db.put("outbox", { ...existing, ...patch });
  notifyOutboxChanged();
}

export async function outboxRemove(id: string) {
  const db = await getDb();
  if (!db) return;
  await db.delete("outbox", id);
  notifyOutboxChanged();
}

/** Every queued mutation across every workspace, oldest first — replay must run in creation order so a
 * comment queued against a task created earlier in the same offline session resolves the task's real
 * (server-assigned) id before the comment is sent (see replay.ts's id-remap step). */
export async function outboxListAll(): Promise<OutboxItem[]> {
  const db = await getDb();
  if (!db) return [];
  return db.getAllFromIndex("outbox", "by-created");
}

export async function outboxListByWorkspace(workspaceId: string): Promise<OutboxItem[]> {
  const db = await getDb();
  if (!db) return [];
  const all = await db.getAllFromIndex("outbox", "by-workspace", workspaceId);
  return all.sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc));
}

export async function conflictAdd(warning: ConflictWarning) {
  const db = await getDb();
  if (!db) return;
  await db.put("conflicts", warning);
}

export async function conflictListByWorkspace(workspaceId: string): Promise<ConflictWarning[]> {
  const db = await getDb();
  if (!db) return [];
  return db.getAllFromIndex("conflicts", "by-workspace", workspaceId);
}

export async function conflictDismiss(id: string) {
  const db = await getDb();
  if (!db) return;
  await db.delete("conflicts", id);
}

/** Test-only: drops and recreates the database so each test file starts clean. */
export async function __resetForTests() {
  const db = await getDb();
  db?.close();
  dbPromise = null;
  await new Promise<void>((resolve, reject) => {
    const req = indexedDB.deleteDatabase(DB_NAME);
    req.onsuccess = () => resolve();
    req.onerror = () => reject(req.error);
    req.onblocked = () => resolve();
  });
}
