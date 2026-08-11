// Block-level file-attachment embed: a DecoratorNode holding only the attachmentId/fileName/contentType/
// sizeBytes returned by the upload endpoint (see DocumentService.UploadAttachmentAsync) — same no-DB-row
// tradeoff ImageNode already accepts (removing the node from the content just orphans its blob, which is
// harmless). Unlike ImageNode, this renders a download link rather than the bytes themselves — the file's
// GET /api/v1/documents/{documentId}/attachments/{attachmentId}/{fileName} response always forces a
// download (see DocumentEndpoints). documentId is intentionally NOT part of the serialized node (see
// DocumentImageContext, reused here) so the content JSON stays portable across copy/paste, templates, and
// version history. Serializes as
// {"type":"file-attachment","attachmentId":"...","fileName":"...","contentType":"...","sizeBytes":...} —
// see LexicalMarkdown.cs's ToMarkdown, which renders this as [fileName](attachment://attachmentId).
import type { LexicalNode, NodeKey, SerializedLexicalNode, Spread } from "lexical";
import { DecoratorNode } from "lexical";
import type { ReactElement } from "react";
import { documentAttachmentHref } from "@/lib/collab/client";
import { useCurrentDocumentId } from "../DocumentImageContext";

export type SerializedFileAttachmentNode = Spread<
  { attachmentId: string; fileName: string; contentType: string; sizeBytes: number },
  SerializedLexicalNode
>;

export class FileAttachmentNode extends DecoratorNode<ReactElement> {
  __attachmentId: string;
  __fileName: string;
  __contentType: string;
  __sizeBytes: number;

  constructor(attachmentId: string, fileName: string, contentType: string, sizeBytes: number, key?: NodeKey) {
    super(key);
    this.__attachmentId = attachmentId;
    this.__fileName = fileName;
    this.__contentType = contentType;
    this.__sizeBytes = sizeBytes;
  }

  static getType(): string {
    return "file-attachment";
  }

  static clone(node: FileAttachmentNode): FileAttachmentNode {
    return new FileAttachmentNode(node.__attachmentId, node.__fileName, node.__contentType, node.__sizeBytes, node.__key);
  }

  createDOM(): HTMLElement {
    const div = document.createElement("div");
    div.className = "my-2";
    return div;
  }

  updateDOM(): boolean {
    return false;
  }

  isInline(): boolean {
    return false;
  }

  exportJSON(): SerializedFileAttachmentNode {
    return {
      type: "file-attachment",
      version: 1,
      attachmentId: this.__attachmentId,
      fileName: this.__fileName,
      contentType: this.__contentType,
      sizeBytes: this.__sizeBytes,
    };
  }

  static importJSON(serializedNode: SerializedFileAttachmentNode): FileAttachmentNode {
    return $createFileAttachmentNode(
      serializedNode.attachmentId,
      serializedNode.fileName,
      serializedNode.contentType,
      serializedNode.sizeBytes,
    );
  }

  getTextContent(): string {
    return this.__fileName;
  }

  decorate(): ReactElement {
    return <FileAttachmentDecorator attachmentId={this.__attachmentId} fileName={this.__fileName} sizeBytes={this.__sizeBytes} />;
  }
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function FileAttachmentDecorator({
  attachmentId,
  fileName,
  sizeBytes,
}: {
  attachmentId: string;
  fileName: string;
  sizeBytes: number;
}) {
  const documentId = useCurrentDocumentId();
  if (!documentId) {
    return null;
  }

  return (
    <a
      href={documentAttachmentHref(documentId, attachmentId, fileName)}
      download={fileName}
      contentEditable={false}
      className="inline-flex items-center gap-2 rounded-md border border-border bg-muted/50 px-3 py-2 text-sm text-foreground no-underline hover:bg-muted"
    >
      <span aria-hidden="true">📎</span>
      <span className="font-medium">{fileName}</span>
      <span className="text-xs text-muted-foreground">{formatFileSize(sizeBytes)}</span>
    </a>
  );
}

export function $createFileAttachmentNode(
  attachmentId: string,
  fileName: string,
  contentType: string,
  sizeBytes: number,
): FileAttachmentNode {
  return new FileAttachmentNode(attachmentId, fileName, contentType, sizeBytes);
}

export function $isFileAttachmentNode(node: LexicalNode | null | undefined): node is FileAttachmentNode {
  return node instanceof FileAttachmentNode;
}
