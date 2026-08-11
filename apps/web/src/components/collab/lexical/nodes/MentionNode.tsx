// @-mention embed: a decorator leaf that renders a workspace member's name as a pill, following the
// exact pattern of TaskReferenceNode.tsx. Serializes as {"type":"mention","userId":"...","name":"..."}
// — see LexicalMarkdown.cs's ToMarkdown, which renders this as @[name](userId) (same wire format as
// the Tiptap comment/description editor's mention node — see mentionExtension.ts).
import type { LexicalNode, NodeKey, SerializedLexicalNode, Spread } from "lexical";
import { DecoratorNode } from "lexical";
import type { ReactElement } from "react";

export type SerializedMentionNode = Spread<{ userId: string; name: string }, SerializedLexicalNode>;

export class MentionNode extends DecoratorNode<ReactElement> {
  __userId: string;
  __name: string;

  constructor(userId: string, name: string, key?: NodeKey) {
    super(key);
    this.__userId = userId;
    this.__name = name;
  }

  static getType(): string {
    return "mention";
  }

  static clone(node: MentionNode): MentionNode {
    return new MentionNode(node.__userId, node.__name, node.__key);
  }

  createDOM(): HTMLElement {
    const span = document.createElement("span");
    span.className = "inline-flex";
    return span;
  }

  updateDOM(): boolean {
    return false;
  }

  isInline(): boolean {
    return true;
  }

  exportJSON(): SerializedMentionNode {
    return { type: "mention", version: 1, userId: this.__userId, name: this.__name };
  }

  static importJSON(serializedNode: SerializedMentionNode): MentionNode {
    return $createMentionNode(serializedNode.userId, serializedNode.name);
  }

  getTextContent(): string {
    return `@${this.__name}`;
  }

  decorate(): ReactElement {
    return (
      <span
        className="mx-0.5 inline-flex items-center rounded bg-primary/10 px-1 py-0.5 text-xs font-medium text-primary"
        contentEditable={false}
      >
        @{this.__name}
      </span>
    );
  }
}

export function $createMentionNode(userId: string, name: string): MentionNode {
  return new MentionNode(userId, name);
}

export function $isMentionNode(node: LexicalNode | null | undefined): node is MentionNode {
  return node instanceof MentionNode;
}
