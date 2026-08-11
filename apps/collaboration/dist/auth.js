/**
 * CRITICAL — the single most important thing here: verifies the connecting user
 * actually has read/edit access to THIS document before Hocuspocus admits them to its room. Forwards the
 * user's own bearer token (obtained browser-side from /api/session/token, the same mechanism the existing
 * SignalR realtime hub already uses — see apps/web/src/lib/realtime/useRealtime.ts) to the .NET API's
 * internal endpoint, which re-runs DocumentService's real membership + Document.CanBeViewedBy checks for
 * that specific document (see DocumentService.CanCollaborateAsync). This is NOT "trust the client": a
 * forged/omitted token or a token for a user without access to this document is always rejected by the
 * .NET side, which is independently integration-tested (DocumentsWikisFlowTests.Can_collaborate_*).
 */
const apiBaseUrl = (process.env.PLANVEXA_API_BASE_URL ?? "http://localhost:8080").replace(/\/$/, "");
export async function checkCollaborationAccess(documentId, workspaceId, token) {
    const response = await fetch(`${apiBaseUrl}/api/v1/internal/documents/${documentId}/can-collaborate`, {
        headers: {
            Authorization: `Bearer ${token}`,
            "X-Workspace": workspaceId,
            Accept: "application/json",
        },
    });
    if (!response.ok) {
        // Any non-2xx (401 bad token, 403 no workspace access, 404 unknown document) is a denial — never
        // treat an API error as "allow by default".
        return { allowed: false, canEdit: false, userId: null };
    }
    const body = (await response.json());
    return { allowed: body.allowed === true, canEdit: body.canEdit === true, userId: body.userId ?? null };
}
/**
 * Whiteboards: the exact same pattern as checkCollaborationAccess above, calling the
 * new .NET internal endpoint that re-runs WhiteboardService's own membership + privacy + linked-resource
 * ACL checks for THIS whiteboard (see WhiteboardService.CanCollaborateAsync). Kept as a separate function
 * (not a parameterized path) so each call site stays a one-line, obviously-correct match to its .NET
 * counterpart — mirrors why Documents' own checkCollaborationAccess isn't parameterized either.
 */
export async function checkWhiteboardCollaborationAccess(whiteboardId, workspaceId, token) {
    const response = await fetch(`${apiBaseUrl}/api/v1/internal/whiteboards/${whiteboardId}/can-collaborate`, {
        headers: {
            Authorization: `Bearer ${token}`,
            "X-Workspace": workspaceId,
            Accept: "application/json",
        },
    });
    if (!response.ok) {
        return { allowed: false, canEdit: false, userId: null };
    }
    const body = (await response.json());
    return { allowed: body.allowed === true, canEdit: body.canEdit === true, userId: body.userId ?? null };
}
