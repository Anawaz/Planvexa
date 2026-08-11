"use client";

// Carries the current document's id down to ImageNode's decorate() output. A DecoratorNode's serialized
// state is the document's own portable content (copy/paste, templates, version history all move it around
// as-is), so the id used to build the image's download URL is threaded through React context by
// PlanvexaEditor instead of being embedded in the node itself.
import { createContext, useContext, type ReactNode } from "react";

const DocumentImageContext = createContext<string | null>(null);

export function DocumentImageProvider({ documentId, children }: { documentId: string; children: ReactNode }) {
  return <DocumentImageContext.Provider value={documentId}>{children}</DocumentImageContext.Provider>;
}

export function useCurrentDocumentId(): string | null {
  return useContext(DocumentImageContext);
}
