import { Server } from "@hocuspocus/server";
import { Database } from "@hocuspocus/extension-database";
import { checkCollaborationAccess, checkWhiteboardCollaborationAccess } from "./auth.js";
import { fetchState, storeState, fetchWhiteboardState, storeWhiteboardState } from "./persistence.js";
const port = Number(process.env.PORT ?? 1234);
/**
 * Whiteboards reuse this exact same Hocuspocus server/pipeline rather than standing up a
 * second collaboration host — the room "kind" is distinguished purely by a documentName prefix the
 * frontend sets when it opens the provider (see apps/web's createWhiteboardProvider vs
 * createDocumentProvider). Documents keep their original unprefixed naming (documentName === the
 * Planvexa Document id) for backward compatibility with rooms/state already in flight; only whiteboard
 * rooms opt into the new "whiteboard:{id}" convention.
 */
const WHITEBOARD_PREFIX = "whiteboard:";
function isWhiteboardRoom(documentName) {
    return documentName.startsWith(WHITEBOARD_PREFIX);
}
function resourceId(documentName) {
    return isWhiteboardRoom(documentName) ? documentName.slice(WHITEBOARD_PREFIX.length) : documentName;
}
const server = new Server({
    port,
    extensions: [
        new Database({
            fetch: async ({ documentName }) => isWhiteboardRoom(documentName) ? fetchWhiteboardState(resourceId(documentName)) : fetchState(documentName),
            store: async ({ documentName, state, lastContext }) => {
                const workspaceId = lastContext?.workspaceId;
                if (!workspaceId) {
                    // onAuthenticate always sets this in context before a room can be joined — if it's missing
                    // here something is deeply wrong upstream, so refuse to write an orphaned row rather than
                    // guess a workspace id.
                    return;
                }
                if (isWhiteboardRoom(documentName)) {
                    await storeWhiteboardState(resourceId(documentName), workspaceId, state);
                }
                else {
                    await storeState(documentName, workspaceId, state);
                }
            },
        }),
    ],
    // CRITICAL: documentName is the Planvexa Document/Whiteboard id (whiteboard
    // ids carry the "whiteboard:" prefix stripped off here) the browser passed as the Yjs document name;
    // requestParameters carries workspaceId (a WebSocket handshake cannot carry a custom header, so it
    // rides the connection URL's query string exactly like the existing SignalR hub already does for its
    // access token).
    async onAuthenticate(data) {
        const { token, documentName, requestParameters, connectionConfig } = data;
        const workspaceId = requestParameters.get("workspaceId");
        if (!token || !workspaceId) {
            throw new Error("Unauthorized: missing token or workspaceId.");
        }
        const access = isWhiteboardRoom(documentName)
            ? await checkWhiteboardCollaborationAccess(resourceId(documentName), workspaceId, token)
            : await checkCollaborationAccess(documentName, workspaceId, token);
        if (!access.allowed) {
            throw new Error("Forbidden: no access to this document.");
        }
        // Read-only participants (guests, or a member without edit rights on a private doc/whiteboard) still
        // get to see live changes but Hocuspocus rejects their writes at the protocol level.
        connectionConfig.readOnly = !access.canEdit;
        const context = { userId: access.userId, workspaceId };
        return context;
    },
});
server.listen();
process.on("SIGTERM", () => void server.destroy());
process.on("SIGINT", () => void server.destroy());
