import { CodeNode } from "@lexical/code";
import { LinkNode } from "@lexical/link";
import { ListItemNode, ListNode } from "@lexical/list";
import { HeadingNode, QuoteNode } from "@lexical/rich-text";
import { TableCellNode, TableNode, TableRowNode } from "@lexical/table";
import type { EditorThemeClasses, Klass, LexicalNode } from "lexical";
import { CalloutNode } from "./nodes/CalloutNode";
import { FileAttachmentNode } from "./nodes/FileAttachmentNode";
import { ImageNode } from "./nodes/ImageNode";
import { MentionNode } from "./nodes/MentionNode";
import { TaskReferenceNode } from "./nodes/TaskReferenceNode";

/**: the full node set the editor supports — headings, lists, code, links, quotes, the
 * custom callout node (see CalloutNode.tsx), the task-reference embed (see TaskReferenceNode.tsx), the
 * @-mention embed (see MentionNode.tsx / MentionsPlugin.tsx), the
 * image embed (see ImageNode.tsx), the file-attachment embed (see FileAttachmentNode.tsx), and tables
 * (@lexical/table's TableNode/TableRowNode/TableCellNode — see LexicalMarkdown.cs's "table" case for
 * export). */
export const editorNodes: ReadonlyArray<Klass<LexicalNode>> = [
  HeadingNode,
  QuoteNode,
  ListNode,
  ListItemNode,
  CodeNode,
  LinkNode,
  CalloutNode,
  TaskReferenceNode,
  MentionNode,
  ImageNode,
  FileAttachmentNode,
  TableNode,
  TableRowNode,
  TableCellNode,
];

export const editorTheme: EditorThemeClasses = {
  heading: {
    h1: "text-2xl font-bold mt-4 mb-2",
    h2: "text-xl font-bold mt-3 mb-2",
    h3: "text-lg font-semibold mt-3 mb-1",
    h4: "text-base font-semibold mt-2 mb-1",
    h5: "text-sm font-semibold mt-2 mb-1",
    h6: "text-sm font-semibold mt-2 mb-1",
  },
  quote: "border-l-4 border-border pl-3 italic text-muted-foreground my-2",
  list: {
    ul: "list-disc pl-6 my-1",
    ol: "list-decimal pl-6 my-1",
    listitem: "my-0.5",
    listitemChecked: "pv-checklist-item pv-checklist-item-checked",
    listitemUnchecked: "pv-checklist-item pv-checklist-item-unchecked",
  },
  link: "text-primary underline underline-offset-2",
  code: "block rounded-md bg-muted px-3 py-2 font-mono text-xs my-2 whitespace-pre-wrap",
  table: "w-full border-collapse my-2 text-sm",
  tableRow: "border-b border-border",
  tableCell: "border border-border px-2 py-1 align-top",
  tableCellHeader: "bg-muted font-semibold",
  text: {
    bold: "font-semibold",
    italic: "italic",
    underline: "underline",
    strikethrough: "line-through",
    code: "rounded bg-muted px-1 py-0.5 font-mono text-xs",
  },
  paragraph: "my-1 leading-6",
};
