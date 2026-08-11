"use client";

import { LexicalComposer } from "@lexical/react/LexicalComposer";
import {
  CollaborationPlugin,
} from "@lexical/react/LexicalCollaborationPlugin";
import { LexicalCollaboration } from "@lexical/react/LexicalCollaborationContext";
import { ContentEditable } from "@lexical/react/LexicalContentEditable";
import { LexicalErrorBoundary } from "@lexical/react/LexicalErrorBoundary";
import { LinkPlugin } from "@lexical/react/LexicalLinkPlugin";
import { ListPlugin } from "@lexical/react/LexicalListPlugin";
import { OnChangePlugin } from "@lexical/react/LexicalOnChangePlugin";
import { RichTextPlugin } from "@lexical/react/LexicalRichTextPlugin";
import type { Doc } from "yjs";
import type { EditorState } from "lexical";
import type { Provider } from "@lexical/yjs";
import { editorNodes, editorTheme } from "./editorConfig";
import { Toolbar } from "./Toolbar";
import { createDocumentProvider } from "@/lib/collab/hocuspocusProvider";

export type PlanvexaEditorProps = {
  documentId: string;
  workspaceId: string;
  initialContent: string;
  userLabel: string;
  canEdit: boolean;
  /** Fired on every local edit (debounced upstream) so the caller can autosave a REST snapshot + version
   * (see useDocumentAutosave) — Yjs/Hocuspocus stays the realtime source of truth; this is. */
  onChange?: (editorStateJson: string) => void;
};

/**
 * The Lexical rich-text editor wired to Yjs/Hocuspocus for realtime collaboration.
 * shouldBootstrap + initialEditorState seed a brand-new (empty) Yjs room from the document's persisted
 * Lexical JSON on first join; once the room has content, Yjs is the source of truth and initialContent is
 * ignored for every subsequent join. Presence/cursors come for free from CollaborationPlugin's awareness
 * wiring (username/cursorColor below) — no extra UI code needed for basic remote-cursor rendering.
 *
 * ponytail: local (non-Yjs-aware) HistoryPlugin for undo/redo — a Yjs-bound UndoManager would coordinate
 * undo across remote edits more precisely; add if plain local undo proves confusing in a multi-editor room.
 */
export function PlanvexaEditor({ documentId, workspaceId, initialContent, userLabel, canEdit, onChange }: PlanvexaEditorProps) {
  return (
    <LexicalComposer
      initialConfig={{
        namespace: `document-${documentId}`,
        theme: editorTheme,
        nodes: [...editorNodes],
        editable: canEdit,
        onError: (error) => console.error("Lexical error", error),
      }}
    >
      <LexicalCollaboration>
        <div className="flex flex-col">
          <Toolbar readOnly={!canEdit} />
          <div className="relative min-h-[28rem] px-4 py-3">
            <RichTextPlugin
              contentEditable={
                <ContentEditable
                  className="min-h-[26rem] text-sm leading-6 outline-none"
                  aria-label="Document content"
                />
              }
              placeholder={<div className="pointer-events-none absolute left-4 top-3 text-sm text-muted-foreground">Start writing…</div>}
              ErrorBoundary={LexicalErrorBoundary}
            />
            <ListPlugin />
            <LinkPlugin />
            {onChange ? (
              <OnChangePlugin
                onChange={(editorState) => onChange(JSON.stringify(editorState.toJSON()))}
              />
            ) : null}
            <CollaborationPlugin
              id={documentId}
              shouldBootstrap
              username={userLabel}
              providerFactory={(id: string, yjsDocMap: Map<string, Doc>) => {
                const provider = createDocumentProvider(id, workspaceId);
                yjsDocMap.set(id, provider.document);
                // HocuspocusProvider's `awareness` is typed `Awareness | null` (awareness can be disabled
                // via config, which we never do); @lexical/yjs's Provider requires non-null. Both
                // implement the same on/off/connect/disconnect/awareness runtime contract that Lexical's
                // collaboration binding actually uses, so this cast reflects a real (if untyped) match.
                return provider as unknown as Provider;
              }}
              initialEditorState={(editor) => {
                try {
                  const state: EditorState = editor.parseEditorState(initialContent);
                  editor.setEditorState(state);
                } catch {
                  // initialContent wasn't valid Lexical JSON (shouldn't happen post-migration, but never
                  // let a bad seed crash the room) — leave the default empty paragraph in place.
                }
              }}
            />
          </div>
        </div>
      </LexicalCollaboration>
    </LexicalComposer>
  );
}
