import { HocuspocusProvider } from "@hocuspocus/provider";

const COLLAB_WS_URL = (process.env.NEXT_PUBLIC_COLLAB_WS_URL ?? "ws://localhost:1234").replace(/\/$/, "");

/**
 * Creates the Yjs provider for a document's collaboration room. The access token rides the
 * WebSocket handshake exactly like the existing SignalR realtime hub (see
 * apps/web/src/lib/realtime/useRealtime.ts's accessTokenFactory comment — browsers cannot set custom
 * headers on a WebSocket handshake). `workspaceId` rides the connection URL's query string for the same
 * reason; the collaboration server (apps/collaboration) forwards both straight to the .NET API's
 * GET /api/v1/internal/documents/{id}/can-collaborate check before admitting the connection to the room —
 * see apps/collaboration/src/auth.ts.
 */
export function createDocumentProvider(documentId: string, workspaceId: string) {
  return new HocuspocusProvider({
    // workspaceId rides the connection URL's query string — the server's onAuthenticatePayload.requestParameters
    // reads it straight off the actual WebSocket request URL regardless of client-side config shape.
    url: `${COLLAB_WS_URL}?workspaceId=${encodeURIComponent(workspaceId)}`,
    name: documentId,
    token: async () => {
      const response = await fetch("/api/session/token", { cache: "no-store" });
      if (!response.ok) {
        throw new Error("Could not obtain an access token for collaboration.");
      }

      return ((await response.json()) as { accessToken: string }).accessToken;
    },
  });
}

/**
 * Whiteboards: the exact same provider setup as createDocumentProvider, targeting a
 * whiteboard's room instead. The "whiteboard:" room-name prefix is how apps/collaboration's single shared
 * Hocuspocus server (see its server.ts) tells a whiteboard room apart from a document room without a
 * second server/port — Document rooms keep their original unprefixed naming for backward compatibility.
 */
export function createWhiteboardProvider(whiteboardId: string, workspaceId: string) {
  return new HocuspocusProvider({
    url: `${COLLAB_WS_URL}?workspaceId=${encodeURIComponent(workspaceId)}`,
    name: `whiteboard:${whiteboardId}`,
    token: async () => {
      const response = await fetch("/api/session/token", { cache: "no-store" });
      if (!response.ok) {
        throw new Error("Could not obtain an access token for collaboration.");
      }

      return ((await response.json()) as { accessToken: string }).accessToken;
    },
  });
}
