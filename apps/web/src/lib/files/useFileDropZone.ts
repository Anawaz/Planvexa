"use client";

// Generic drag-and-drop file intake for plain HTML containers (attachment lists, comment
// composers, chat composers). Mirrors the drop handling ImagePlugin.tsx already does for images
// dropped into document content, but for a bare <div>/<form> instead of a Lexical editor: swallow
// dragover/drop so the browser doesn't navigate to show the dropped file, and hand the files to the
// caller's existing upload path unchanged.
import { useState, type DragEvent } from "react";

function containsFiles(event: DragEvent): boolean {
  return Array.from(event.dataTransfer?.types ?? []).includes("Files");
}

export type FileDropZoneProps = {
  onDragEnter: (event: DragEvent) => void;
  onDragOver: (event: DragEvent) => void;
  onDragLeave: (event: DragEvent) => void;
  onDrop: (event: DragEvent) => void;
};

export function useFileDropZone(
  onFiles: (files: File[]) => void,
  disabled = false,
): { isDraggingOver: boolean; dropZoneProps: FileDropZoneProps } {
  const [isDraggingOver, setIsDraggingOver] = useState(false);

  return {
    isDraggingOver,
    dropZoneProps: {
      onDragEnter(event) {
        if (disabled || !containsFiles(event)) return;
        event.preventDefault();
        setIsDraggingOver(true);
      },
      onDragOver(event) {
        if (disabled || !containsFiles(event)) return;
        // Required on every dragover, not just dragenter, or the browser rejects the drop.
        event.preventDefault();
      },
      onDragLeave(event) {
        // Ignore bubbling from a child element back into the same drop zone.
        if (event.currentTarget.contains(event.relatedTarget as Node | null)) return;
        setIsDraggingOver(false);
      },
      onDrop(event) {
        setIsDraggingOver(false);
        if (disabled) return;
        const files = Array.from(event.dataTransfer?.files ?? []);
        if (files.length === 0) return;
        event.preventDefault();
        onFiles(files);
      },
    },
  };
}
