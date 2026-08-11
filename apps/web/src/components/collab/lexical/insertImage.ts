import type { LexicalEditor } from "lexical";
import { $getSelection, $isRangeSelection } from "lexical";
import { uploadDocumentImage } from "@/lib/collab/client";
import { $createImageNode } from "./nodes/ImageNode";

/** Uploads a File through the document's image endpoint and inserts the resulting ImageNode at the
 * current selection — shared by the toolbar's Image button, and by ImagePlugin's paste/drag-drop
 * handling. */
export async function insertImageFromFile(editor: LexicalEditor, documentId: string, file: File): Promise<void> {
  const { imageId, contentType } = await uploadDocumentImage(documentId, file);
  editor.update(() => {
    const selection = $getSelection();
    if ($isRangeSelection(selection)) {
      selection.insertNodes([$createImageNode(imageId, contentType)]);
    }
  });
}
