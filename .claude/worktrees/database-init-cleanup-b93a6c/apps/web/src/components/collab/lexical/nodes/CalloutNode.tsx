// Lexical has no built-in callout node, so this is a minimal custom ElementNode (not a
// styled blockquote variant) — block-level, holds inline/paragraph children like QuoteNode, renders as a
// tinted panel. Kept deliberately small: no variant/icon system, just one visual style. Serializes as
// {"type":"callout", ...SerializedElementNode} — see LexicalMarkdown.cs's ToMarkdown, which renders this
// node type as a GitHub-style "> [!NOTE]" blockquote on export.
import type {
  DOMConversionMap,
  DOMConversionOutput,
  DOMExportOutput,
  EditorConfig,
  LexicalNode,
  ParagraphNode,
  RangeSelection,
  SerializedElementNode,
} from "lexical";
import { $applyNodeReplacement, $createParagraphNode, ElementNode } from "lexical";

export type SerializedCalloutNode = SerializedElementNode;

export class CalloutNode extends ElementNode {
  static getType(): string {
    return "callout";
  }

  static clone(node: CalloutNode): CalloutNode {
    return new CalloutNode(node.__key);
  }

  createDOM(config: EditorConfig): HTMLElement {
    void config;
    const dom = document.createElement("div");
    dom.className =
      "callout-node rounded-lg border border-amber-300 bg-amber-50 px-4 py-3 text-sm dark:border-amber-900 dark:bg-amber-950";
    return dom;
  }

  updateDOM(): boolean {
    return false;
  }

  exportDOM(): DOMExportOutput {
    const element = document.createElement("div");
    element.setAttribute("data-lexical-callout", "true");
    return { element };
  }

  static importDOM(): DOMConversionMap | null {
    return {
      div: (domNode: HTMLElement) => {
        if (!domNode.hasAttribute("data-lexical-callout")) {
          return null;
        }

        return {
          conversion: (): DOMConversionOutput => ({ node: $createCalloutNode() }),
          priority: 1,
        };
      },
    };
  }

  exportJSON(): SerializedCalloutNode {
    return { ...super.exportJSON(), type: "callout", version: 1 };
  }

  static importJSON(serializedNode: SerializedCalloutNode): CalloutNode {
    return $createCalloutNode().updateFromJSON(serializedNode);
  }

  insertNewAfter(_: RangeSelection, restoreSelection = true): ParagraphNode {
    const newParagraph = $createParagraphNode();
    const direction = this.getDirection();
    newParagraph.setDirection(direction);
    this.insertAfter(newParagraph, restoreSelection);
    return newParagraph;
  }

  collapseAtStart(): true {
    const paragraph = $createParagraphNode();
    const children = this.getChildren();
    children.forEach((child) => paragraph.append(child));
    this.replace(paragraph);
    return true;
  }

  canMergeWhenEmpty(): true {
    return true;
  }
}

export function $createCalloutNode(): CalloutNode {
  return $applyNodeReplacement(new CalloutNode());
}

export function $isCalloutNode(node: LexicalNode | null | undefined): node is CalloutNode {
  return node instanceof CalloutNode;
}
