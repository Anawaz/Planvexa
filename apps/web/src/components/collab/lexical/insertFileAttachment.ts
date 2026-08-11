import type { LexicalEditor } from "lexical";
import { $getSelection, $isRangeSelection } from "lexical";
import { uploadDocumentAttachment } from "@/lib/collab/client";
import { $createFileAttachmentNode } from "./nodes/FileAttachmentNode";

/** Uploads a File through the document's attachment endpoint and inserts the resulting
 * FileAttachmentNode at the current selection — used by the toolbar's Attach button. */
export async function insertFileAttachmentFromFile(editor: LexicalEditor, documentId: string, file: File): Promise<void> {
  const { attachmentId, fileName, contentType, sizeBytes } = await uploadDocumentAttachment(documentId, file);
  editor.update(() => {
    const selection = $getSelection();
    if ($isRangeSelection(selection)) {
      selection.insertNodes([$createFileAttachmentNode(attachmentId, fileName, contentType, sizeBytes)]);
    }
  });
}
