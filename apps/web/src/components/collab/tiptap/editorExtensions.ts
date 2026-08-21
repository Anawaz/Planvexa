import { Placeholder } from "@tiptap/extensions";
import { TaskItem, TaskList } from "@tiptap/extension-list";
import { Table, TableCell, TableHeader, TableRow } from "@tiptap/extension-table";
import StarterKit from "@tiptap/starter-kit";
import { Markdown } from "tiptap-markdown";
import type { AnyExtension } from "@tiptap/core";

/**
 * The editor's node/mark vocabulary, in one place.
 *
 * Extracted from the component so the markdown round trip can be tested headlessly against the exact
 * same set. That matters more here than it usually would: descriptions and comments are stored as a
 * markdown STRING and rendered by re-mounting this editor read-only, so a node Tiptap can create but
 * tiptap-markdown cannot serialize is silent data loss on save. The test asserting that is only
 * meaningful if it uses the real list rather than a copy that can drift.
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
    Placeholder.configure({ placeholder: placeholder ?? "" }),
    Markdown.configure({ html: false, transformPastedText: true }),
  ];
}
