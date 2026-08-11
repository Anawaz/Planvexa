"use client";

import { useCallback, useEffect, useRef } from "react";
import { updateDocument } from "@/lib/collab/client";

const IDLE_SAVE_MS = 8_000;
const MAX_INTERVAL_MS = 2 * 60_000;

/**
 * Autosave. Yjs/Hocuspocus is the realtime source of truth for the live edit session
 * (apps/collaboration persists its own Y.Doc state to docs.document_collab_state on its own debounce), but
 * version history still needs meaningful checkpoints — so the browser also periodically PATCHes the
 * resolved Lexical JSON through the existing REST endpoint, which both writes docs.documents.content AND
 * appends a DocumentVersion (Document.Update already does this whenever content changes — see
 * DocumentService.UpdateAsync), without any new backend surface.
 *
 * Two triggers: debounced idle save (IDLE_SAVE_MS after the last keystroke) so a pause after a small edit
 * still saves promptly, and a hard ceiling (MAX_INTERVAL_MS) so continuous typing without a pause still
 * checkpoints every couple of minutes rather than only on tab close.
 */
export function useDocumentAutosave(documentId: string, enabled: boolean) {
  const idleTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const intervalTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const latestJson = useRef<string | null>(null);
  const savedJson = useRef<string | null>(null);

  const flush = useCallback(() => {
    if (latestJson.current === null || latestJson.current === savedJson.current) {
      return;
    }

    const content = latestJson.current;
    savedJson.current = content;
    void updateDocument(documentId, { content }).catch(() => {
      // A failed autosave just gets retried on the next change/interval tick — Yjs still holds the live
      // state so no edits are lost, only the version-history checkpoint is delayed.
      savedJson.current = null;
    });
  }, [documentId]);

  useEffect(
    () => () => {
      if (idleTimer.current) clearTimeout(idleTimer.current);
      if (intervalTimer.current) clearTimeout(intervalTimer.current);
    },
    [],
  );

  const onChange = useCallback(
    (editorStateJson: string) => {
      if (!enabled) return;
      latestJson.current = editorStateJson;

      if (idleTimer.current) clearTimeout(idleTimer.current);
      idleTimer.current = setTimeout(flush, IDLE_SAVE_MS);

      if (!intervalTimer.current) {
        intervalTimer.current = setTimeout(() => {
          intervalTimer.current = null;
          flush();
        }, MAX_INTERVAL_MS);
      }
    },
    [enabled, flush],
  );

  return onChange;
}
