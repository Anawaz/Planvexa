import { Placeholder } from "@tiptap/extensions";
import { TaskItem, TaskList } from "@tiptap/extension-list";
import { Table, TableCell, TableHeader, TableRow } from "@tiptap/extension-table";
import Image from "@tiptap/extension-image";
import Subscript from "@tiptap/extension-subscript";
import Superscript from "@tiptap/extension-superscript";
import StarterKit from "@tiptap/starter-kit";
import { Markdown } from "tiptap-markdown";
import type { AnyExtension } from "@tiptap/core";

/**
 * The editor's node/mark vocabulary, in one place.
 *
 * Extracted from the component so the markdown round trip can be tested headlessly against the exact
 * same set. That matters more here than it usually would: descriptions and comments are stored as a
 * markdown STRING and rendered by re-mounting this editor read-only, so a node Tiptap can create but
 * tiptap-markdown cannot serialize is silent data loss on save. Every entry below was verified to
 * survive parse → serialize before being given a toolbar control.
 *
 * Mentions are deliberately NOT here — that extension needs a live member directory and belongs to the
 * component.
 */
export function createEditorExtensions(placeholder?: string): AnyExtension[] {
  return [
    StarterKit,
    // Checkbox lists and tables round-trip as plain GFM (tiptap-markdown's task-list.js / table.js).
    // `resizable: false` because column widths would be stored as HTML attributes markdown cannot
    // carry — they would silently vanish on the next save.
    TaskList,
    TaskItem.configure({ nested: true }),
    Table.configure({ resizable: false }),
    TableRow,
    TableHeader,
    TableCell,
    // `![alt](src)` — ordinary markdown, no HTML needed.
    Image.configure({ inline: false, allowBase64: false }),
    // Markdown has no syntax for these three, so they round-trip as the HTML tags GitHub and GitLab
    // also accept (`<u>`, `<sup>`, `<sub>`) — which is why `html: true` is set below.
    Superscript,
    Subscript,
    Placeholder.configure({ placeholder: placeholder ?? "" }),
    // html: true is what lets underline/superscript/subscript survive. It is NOT a raw-HTML
    // passthrough: markdown-it hands the HTML to ProseMirror's DOMParser, which only ever produces
    // nodes and marks this schema declares — anything else collapses to its text content, and nothing
    // is inserted via innerHTML at any point. (Verified: a `<details>` block comes back as plain text
    // rather than as markup, which is also why there is no Collapsible section control.)
    Markdown.configure({ html: true, transformPastedText: true }),
  ];
}
