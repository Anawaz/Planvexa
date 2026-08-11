"use client";

// Paste/drag-drop image insertion: intercepts PASTE_COMMAND/DROP_COMMAND before Lexical's default
// handling, uploads any image files found on the clipboard/drop payload (see insertImageFromFile — the
// same upload+insert path the toolbar's Image button uses), and swallows the event so no other plugin
// tries to insert the raw image bytes as text/HTML.
import { useEffect } from "react";
import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import { COMMAND_PRIORITY_LOW, DROP_COMMAND, PASTE_COMMAND } from "lexical";
import { insertImageFromFile } from "./insertImage";

function imageFilesFrom(data: DataTransfer | null): File[] {
  if (!data) return [];
  return Array.from(data.files).filter((file) => file.type.startsWith("image/"));
}

export function ImagePlugin({ documentId }: { documentId: string }) {
  const [editor] = useLexicalComposerContext();

  useEffect(() => {
    const unregisterPaste = editor.registerCommand(
      PASTE_COMMAND,
      (event) => {
        const data = event instanceof ClipboardEvent ? event.clipboardData : null;
        const files = imageFilesFrom(data);
        if (files.length === 0) return false;

        event.preventDefault();
        for (const file of files) {
          void insertImageFromFile(editor, documentId, file);
        }

        return true;
      },
      COMMAND_PRIORITY_LOW,
    );

    const unregisterDrop = editor.registerCommand(
      DROP_COMMAND,
      (event) => {
        const files = imageFilesFrom(event.dataTransfer);
        if (files.length === 0) return false;

        event.preventDefault();
        for (const file of files) {
          void insertImageFromFile(editor, documentId, file);
        }

        return true;
      },
      COMMAND_PRIORITY_LOW,
    );

    return () => {
      unregisterPaste();
      unregisterDrop();
    };
  }, [editor, documentId]);

  return null;
}
