"use client";

import { useCallback, useEffect, useRef, useSyncExternalStore } from "react";
import { HttpTransportType, HubConnection, HubConnectionBuilder } from "@microsoft/signalr";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import { useAppContext } from "@/lib/app-context/AppContext";
import { chatKeys } from "@/lib/chat/queries";
import { collabKeys } from "@/lib/collab/queries";
import { replayOutbox } from "@/lib/offline/replay";
import { planningKeys } from "@/lib/planning/queries";
import { timeKeys } from "@/lib/time/queries";
import { workKeys } from "@/lib/work/queries";

const API_BASE = (process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080").replace(/\/$/, "");

/** Envelope broadcast by the API's IRealtimeNotifier (camelCase over the SignalR JSON protocol). */
type EntityChangedEvent = {
  workspaceId: string;
  entityType: string;
  entityId: string;
  action: string;
  version: number | null;
  correlationId: string;
};

type PresenceEvent = { workspaceId: string; userIds: string[] };

type TypingEvent = { workspaceId: string; resourceType: string; resourceId: string; userId: string; expiresAtUtc: string };

// ponytail: same module-level-store trick as presence above — a typing ping should re-render only the
// small indicator reading it, not the whole authenticated subtree.
const EMPTY_TYPING: readonly string[] = [];
const typingSnapshots = new Map<string, readonly string[]>();
const typingExpiryByResource = new Map<string, Map<string, number>>();
const typingListeners = new Set<() => void>();
let activeConnection: HubConnection | null = null;

function notifyTypingListeners() {
  for (const listener of typingListeners) listener();
}

function rebuildTypingSnapshot(key: string) {
  const users = typingExpiryByResource.get(key);
  typingSnapshots.set(key, users && users.size > 0 ? Array.from(users.keys()) : EMPTY_TYPING);
}

/** Drops expired entries so a dropped connection or an idle typist does not leave a stale indicator. */
function pruneExpiredTyping() {
  const now = Date.now();
  let changed = false;
  for (const [key, users] of typingExpiryByResource) {
    for (const [userId, expiresAtMs] of users) {
      if (expiresAtMs <= now) {
        users.delete(userId);
        changed = true;
      }
    }

    if (changed) {
      rebuildTypingSnapshot(key);
    }
  }

  if (changed) {
    notifyTypingListeners();
  }
}

if (typeof window !== "undefined") {
  window.setInterval(pruneExpiredTyping, 1000);
}

function recordTyping(event: TypingEvent) {
  const key = `${event.resourceType}:${event.resourceId}`;
  const users = typingExpiryByResource.get(key) ?? new Map<string, number>();
  users.set(event.userId, new Date(event.expiresAtUtc).getTime());
  typingExpiryByResource.set(key, users);
  rebuildTypingSnapshot(key);
  notifyTypingListeners();
}

/** User ids (excluding the caller — the hub does not echo back) currently typing in one resource. */
export function useTypingUsers(resourceType: string, resourceId: string | null | undefined): readonly string[] {
  const key = `${resourceType}:${resourceId ?? ""}`;
  return useSyncExternalStore(
    (listener) => {
      typingListeners.add(listener);
      return () => typingListeners.delete(listener);
    },
    () => (resourceId ? (typingSnapshots.get(key) ?? EMPTY_TYPING) : EMPTY_TYPING),
    () => EMPTY_TYPING,
  );
}

/**
 * Broadcasts "I am typing" for one resource, throttled to at most once per {@link throttleMs} so a
 * fast typist does not spam the hub on every keystroke. Call the returned function from the composer's
 * onChange; there is no explicit "stopped typing" signal — the recipient's indicator simply expires
 * (see the server's TypingTtl) once pings stop.
 */
export function useTypingBroadcast(workspaceId: string | null | undefined, resourceType: string, resourceId: string | null | undefined) {
  const lastSentRef = useRef(0);
  const throttleMs = 3000;

  return useCallback(() => {
    if (!activeConnection || !workspaceId || !resourceId) {
      return;
    }

    const now = Date.now();
    if (now - lastSentRef.current < throttleMs) {
      return;
    }

    lastSentRef.current = now;
    void activeConnection.invoke("Typing", workspaceId, resourceType, resourceId).catch(() => undefined);
  }, [workspaceId, resourceType, resourceId]);
}

// ponytail: module-level store instead of a context provider — presence changes then re-render only
// PresenceAvatars, not the whole authenticated subtree. Single hub connection makes this safe.
let presenceUserIds: readonly string[] = [];
const presenceListeners = new Set<() => void>();

function setPresence(userIds: readonly string[]) {
  presenceUserIds = userIds;
  for (const listener of presenceListeners) listener();
}

/** Live presence user ids for the current workspace; empty until the hub reports a roster. */
export function useRealtimePresence(): readonly string[] {
  return useSyncExternalStore(
    (listener) => {
      presenceListeners.add(listener);
      return () => presenceListeners.delete(listener);
    },
    () => presenceUserIds,
    () => presenceUserIds,
  );
}

/**
 * Maps a backend entityType to the query roots it invalidates. The API emits exactly these five
 * (grep `new RealtimeEvent(`): Task, Comment, ChatChannel, ChatMessage, TimeEntry. Unknown types are
 * ignored rather than triggering a blanket invalidate — a future emitter gets an entry here.
 */
function invalidateFor(queryClient: QueryClient, event: EntityChangedEvent, workspaceId: string) {
  const invalidate = (queryKey: readonly unknown[]) => void queryClient.invalidateQueries({ queryKey });

  switch (event.entityType) {
    case "Task":
      invalidate(workKeys.all);
      invalidate(planningKeys.all);
      break;
    case "Comment":
      // entityId is the comment id, not the task id, so invalidate every task's comment list.
      invalidate(collabKeys.commentsRoot());
      invalidate(collabKeys.unreadCount());
      invalidate(collabKeys.notificationsRoot());
      break;
    case "ChatChannel":
      invalidate(chatKeys.channels(workspaceId));
      break;
    case "ChatMessage":
      invalidate(chatKeys.messagesRoot(workspaceId));
      break;
    case "TimeEntry":
      invalidate(timeKeys.all);
      break;
    default:
      break;
  }
}

/**
 * Single workspace hub connection for the authenticated shell. Mount once (app/app/layout.tsx).
 *
 * skipNegotiation + WebSockets goes straight to the API, so no CORS preflight is involved; the
 * access token rides the query string (the API's JwtBearer OnMessageReceived reads it for /hubs)
 * because browsers cannot set headers on a WebSocket handshake.
 */
export function useRealtime() {
  const queryClient = useQueryClient();
  const { workspaceId } = useAppContext();

  useEffect(() => {
    if (!workspaceId) return;

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/workspace`, {
        skipNegotiation: true,
        transport: HttpTransportType.WebSockets,
        accessTokenFactory: async () => {
          const response = await fetch("/api/session/token", { cache: "no-store" });
          if (!response.ok) throw new Error("Could not obtain an access token for realtime.");
          return ((await response.json()) as { accessToken: string }).accessToken;
        },
      })
      .withAutomaticReconnect()
      .build();

    connection.on("entityChanged", (event: EntityChangedEvent) => {
      // Never act on an event from another workspace (e.g. one in flight across a switch).
      if (event.workspaceId !== workspaceId) return;
      invalidateFor(queryClient, event, workspaceId);
    });

    connection.on("presence", (event: PresenceEvent) => {
      if (event.workspaceId !== workspaceId) return;
      setPresence(event.userIds ?? []);
    });

    connection.on("typing", (event: TypingEvent) => {
      if (event.workspaceId !== workspaceId) return;
      recordTyping(event);
    });

    connection.onreconnected(() => {
      void connection.invoke("JoinWorkspace", workspaceId).catch(() => undefined);
      // Events missed while disconnected are unrecoverable; refetch everything on screen.
      void queryClient.invalidateQueries();
      // A SignalR reconnect is one of the two reconnect signals the offline outbox replays
      // on (the other is the browser `online` event, wired in useOfflineSync.ts) — this hub only
      // reconnects when the network is actually back, so it doubles as a reliable trigger.
      void replayOutbox(queryClient);
    });

    activeConnection = connection;

    let disposed = false;
    void connection
      .start()
      .then(() => (disposed ? undefined : connection.invoke("JoinWorkspace", workspaceId)))
      .catch(() => undefined);

    return () => {
      disposed = true;
      setPresence([]);
      if (activeConnection === connection) {
        activeConnection = null;
      }

      void connection.stop();
    };
    // Workspace membership is baked into the connection and group membership: rebuild on change
    // rather than bookkeeping Leave/Join.
  }, [workspaceId, queryClient]);
}
