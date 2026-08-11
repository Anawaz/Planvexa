import { Pool } from "pg";

/**
 * Yjs document state persistence. One row per document in docs.document_collab_state
 * (see DB script 0057_AddDocumentCollabState.sql) holding the latest merged Y.Doc binary update.
 *
 * Why Postgres and not Redis: the dev/prod infra stack (docker-compose.yml, apps/apphost/AppHost.cs)
 * provisions Keycloak/Mailpit/Jaeger + PostgreSQL only — no Redis is available, and introducing one
 * purely for this would be a new infra dependency, when an
 * in-memory-with-periodic-Postgres-flush approach works fine. Hocuspocus's in-process Y.Doc IS the
 * in-memory working copy for an active room; this module is only the periodic flush + resume-on-restart
 * layer, debounced by @hocuspocus/extension-database's own internal timer (not reimplemented here).
 *
 * This table is a resumable working buffer, not the durable version history — DocumentVersion rows
 * (created via the .NET REST autosave path, see the frontend's useDocumentAutosave hook) remain the
 * source of truth for human-browsable history.
 */
const pool = new Pool({
  connectionString:
    process.env.PLANVEXA_COLLAB_DB_URL ??
    "postgresql://planvexa:planvexa@localhost:5432/planvexa",
});

export async function fetchState(documentId: string): Promise<Uint8Array | null> {
  const result = await pool.query<{ y_state: Buffer }>(
    "SELECT y_state FROM docs.document_collab_state WHERE document_id = $1",
    [documentId],
  );
  const row = result.rows[0];
  return row ? new Uint8Array(row.y_state) : null;
}

export async function storeState(documentId: string, workspaceId: string, state: Uint8Array): Promise<void> {
  await pool.query(
    `INSERT INTO docs.document_collab_state (document_id, workspace_id, y_state, updated_at_utc)
     VALUES ($1, $2, $3, now())
     ON CONFLICT (document_id)
     DO UPDATE SET y_state = excluded.y_state, updated_at_utc = excluded.updated_at_utc`,
    [documentId, workspaceId, Buffer.from(state)],
  );
}

/**
 * Whiteboards: the exact same fetch/store pair as Documents' above, targeting
 * whiteboards.whiteboard_collab_state instead (see 0067_AddWhiteboards.sql's header — same shape/purpose
 * as docs.document_collab_state, one row per whiteboard holding the latest merged Yjs binary update).
 */
export async function fetchWhiteboardState(whiteboardId: string): Promise<Uint8Array | null> {
  const result = await pool.query<{ y_state: Buffer }>(
    "SELECT y_state FROM whiteboards.whiteboard_collab_state WHERE whiteboard_id = $1",
    [whiteboardId],
  );
  const row = result.rows[0];
  return row ? new Uint8Array(row.y_state) : null;
}

export async function storeWhiteboardState(whiteboardId: string, workspaceId: string, state: Uint8Array): Promise<void> {
  await pool.query(
    `INSERT INTO whiteboards.whiteboard_collab_state (whiteboard_id, workspace_id, y_state, updated_at_utc)
     VALUES ($1, $2, $3, now())
     ON CONFLICT (whiteboard_id)
     DO UPDATE SET y_state = excluded.y_state, updated_at_utc = excluded.updated_at_utc`,
    [whiteboardId, workspaceId, Buffer.from(state)],
  );
}

export async function closePool(): Promise<void> {
  await pool.end();
}
