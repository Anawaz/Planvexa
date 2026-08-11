// Block-level image embed: a DecoratorNode holding only the imageId/contentType returned by the upload
// endpoint (see DocumentService.UploadImageAsync) — no independent DB row, same no-lifecycle tradeoff
// WhiteboardService.UploadImageAsync already accepts for canvas images (an image later removed from the
// content just orphans its blob, which is harmless). The bytes are fetched from
// GET /api/v1/documents/{documentId}/images/{imageId} (documentImageHref) at render time; documentId is
// intentionally NOT part of the serialized node (see DocumentImageContext) so the content JSON stays
// portable across copy/paste, templates, and version history. Serializes as
// {"type":"image","imageId":"...","contentType":"...","altText":"..."} — see LexicalMarkdown.cs's
// ToMarkdown, which renders this as ![altText](image://imageId).
import type { LexicalNode, NodeKey, SerializedLexicalNode, Spread } from "lexical";
import { DecoratorNode } from "lexical";
import type { ReactElement } from "react";
import { documentImageHref } from "@/lib/collab/client";
import { useCurrentDocumentId } from "../DocumentImageContext";

export type SerializedImageNode = Spread<
  { imageId: string; contentType: string; altText: string },
  SerializedLexicalNode
>;

export class ImageNode extends DecoratorNode<ReactElement> {
  __imageId: string;
  __contentType: string;
  __altText: string;

  constructor(imageId: string, contentType: string, altText = "", key?: NodeKey) {
    super(key);
    this.__imageId = imageId;
    this.__contentType = contentType;
    this.__altText = altText;
  }

  static getType(): string {
    return "image";
  }

  static clone(node: ImageNode): ImageNode {
    return new ImageNode(node.__imageId, node.__contentType, node.__altText, node.__key);
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

  exportJSON(): SerializedImageNode {
    return { type: "image", version: 1, imageId: this.__imageId, contentType: this.__contentType, altText: this.__altText };
  }

  static importJSON(serializedNode: SerializedImageNode): ImageNode {
    return $createImageNode(serializedNode.imageId, serializedNode.contentType, serializedNode.altText);
  }

  getTextContent(): string {
    return this.__altText;
  }

  decorate(): ReactElement {
    return <ImageDecorator imageId={this.__imageId} altText={this.__altText} />;
  }
}

function ImageDecorator({ imageId, altText }: { imageId: string; altText: string }) {
  const documentId = useCurrentDocumentId();
  if (!documentId) {
    return null;
  }

  return (
    // eslint-disable-next-line @next/next/no-img-element -- authenticated proxy URL, next/image can't fetch it.
    <img
      src={documentImageHref(documentId, imageId)}
      alt={altText}
      className="max-w-full rounded-md border border-border"
      contentEditable={false}
    />
  );
}

export function $createImageNode(imageId: string, contentType: string, altText = ""): ImageNode {
  return new ImageNode(imageId, contentType, altText);
}

export function $isImageNode(node: LexicalNode | null | undefined): node is ImageNode {
  return node instanceof ImageNode;
}
