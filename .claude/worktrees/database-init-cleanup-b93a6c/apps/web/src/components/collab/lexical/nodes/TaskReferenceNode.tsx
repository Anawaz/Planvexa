// Embeds: a decorator leaf that links to a Task by id and renders its title, fetched via
// the existing task API (lib/work/client.getTask) at insertion time and cached in the node's own
// serialized state (title) so the document renders without a round trip on every load — a stale cached
// title (if the task is later renamed) is an acceptable simplification; the link always points at the
// live task. Serializes as {"type":"task-reference","taskId":"...","title":"..."} — see
// LexicalMarkdown.cs's ToMarkdown, which renders this as [title](task://taskId).
import type { LexicalNode, NodeKey, SerializedLexicalNode, Spread } from "lexical";
import { DecoratorNode } from "lexical";
import Link from "next/link";
import type { ReactElement } from "react";

export type SerializedTaskReferenceNode = Spread<
  { taskId: string; title: string },
  SerializedLexicalNode
>;

export class TaskReferenceNode extends DecoratorNode<ReactElement> {
  __taskId: string;
  __title: string;

  constructor(taskId: string, title: string, key?: NodeKey) {
    super(key);
    this.__taskId = taskId;
    this.__title = title;
  }

  static getType(): string {
    return "task-reference";
  }

  static clone(node: TaskReferenceNode): TaskReferenceNode {
    return new TaskReferenceNode(node.__taskId, node.__title, node.__key);
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

  exportJSON(): SerializedTaskReferenceNode {
    return { type: "task-reference", version: 1, taskId: this.__taskId, title: this.__title };
  }

  static importJSON(serializedNode: SerializedTaskReferenceNode): TaskReferenceNode {
    return $createTaskReferenceNode(serializedNode.taskId, serializedNode.title);
  }

  getTextContent(): string {
    return this.__title;
  }

  decorate(): ReactElement {
    return (
      <Link
        href={`/app/tasks/${this.__taskId}`}
        className="mx-0.5 inline-flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary hover:bg-primary/20"
        contentEditable={false}
      >
        ✓ {this.__title}
      </Link>
    );
  }
}

export function $createTaskReferenceNode(taskId: string, title: string): TaskReferenceNode {
  return new TaskReferenceNode(taskId, title);
}

export function $isTaskReferenceNode(node: LexicalNode | null | undefined): node is TaskReferenceNode {
  return node instanceof TaskReferenceNode;
}
