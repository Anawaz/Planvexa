"use client";

import type { Editor } from "@tiptap/react";
import { useState } from "react";
import { useDismissable } from "@/components/ui/ActionMenu";
import { cn } from "@/lib/utils";

/**
 * The formatting bar for task descriptions and comments, modelled on GitLab's Content Editor (the same
 * Tiptap/ProseMirror engine, so the same controls are available).
 *
 * Every control has to survive a round trip through markdown — the content is stored as a markdown
 * string in the existing description/comment-body columns, and it is RENDERED by mounting this same
 * editor read-only (see CommentItem). A control is therefore only worth having if tiptap-markdown can
 * serialize it and Tiptap can parse it back, which BasicRichTextEditor.roundtrip.test.tsx pins down
 * for every one of them.
 *
 * Two of GitLab's items are absent because each needs a custom Tiptap node, not a button — shipping
 * them as raw insertions loses the content on the next save:
 *
 * - "Alert" (`> [!NOTE]`): the serializer escapes the marker to `\[!NOTE\]` and folds the line break,
 *   so it degrades to an ordinary quote. Needs a node with a raw-writing serializer.
 * - "Collapsible section": `<details>` has no node in this schema, so ProseMirror's parser reduces it
 *   to its text content and the markup is gone. Needs a details/summary node pair.
 *
 * Two more are GitLab-product-specific and have nothing to point at here: "Embedded view" (its
 * metrics/chart embeds) and "Table of contents" (`[[_TOC_]]`, which nothing in Planvexa renders).
 *
 * Mermaid and PlantUML insert fenced code blocks with the right language tag — exactly what GitLab
 * stores — and display as code until a diagram renderer exists.
 */

const buttonClass =
  "grid size-7 shrink-0 place-items-center rounded text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-40 motion-reduce:transition-none";

/** One 24-grid stroke path per glyph, matching the app's own icon set (app-shell/icons.tsx). */
const icons = {
  undo: "M9 14l-4-4 4-4M5 10h9a5 5 0 010 10h-3",
  redo: "M15 14l4-4-4-4M19 10h-9a5 5 0 000 10h3",
  bold: "M7 5h6a3.5 3.5 0 010 7H7zM7 12h7a3.5 3.5 0 010 7H7z",
  italic: "M14 5h-4M14 5l-4 14M10 19H6",
  underline: "M7 4v7a5 5 0 0010 0V4M5 20h14",
  strike: "M5 12h14M8 8.5A3 3 0 0111 6h2a3 3 0 012.5 1.4M16 15a3 3 0 01-3 2.5h-2A3 3 0 018 15.5",
  superscript: "M4 6l7 12M11 6L4 18M17 10V7h3M17 7a2 2 0 013 1.5c0 1.5-3 2-3 3.5h3",
  subscript: "M4 5l7 12M11 5L4 17M17 20v-3h3M17 17a2 2 0 013 1.5c0 1.5-3 2-3 3.5h3",
  quote: "M5 6h14M5 12h9M5 18h9M18 11v8",
  code: "M9 8l-4 4 4 4M15 8l4 4-4 4",
  link: "M10 13a4 4 0 006 .5l2-2a4 4 0 00-5.7-5.7l-1 1M14 11a4 4 0 00-6-.5l-2 2a4 4 0 005.7 5.7l1-1",
  clearFormat: "M7 6h12M11 6L8 18M15 18h5M4 4l16 16",
  bulletList: "M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01",
  orderedList: "M10 6h10M10 12h10M10 18h10M4 5h1v4M4 9h2M4 13h2v2H4v2h2",
  taskList: "M10 6h10M10 12h10M10 18h10M3.5 6l1.2 1.2L7 5M3.5 12l1.2 1.2L7 11M3.5 18l1.2 1.2L7 17",
  outdent: "M10 6h10M10 12h10M10 18h10M7 9l-3 3 3 3",
  indent: "M10 6h10M10 12h10M10 18h10M4 9l3 3-3 3",
  table: "M4 5h16v14H4zM4 10h16M4 15h16M9.5 5v14M14.5 5v14",
  image: "M4 5h16v14H4zM4 16l4.5-4.5 3 3L15 11l5 5M15.5 8.5h.01",
  attach: "M17 8l-6.5 6.5a2.5 2.5 0 003.5 3.5L21 11a4.5 4.5 0 00-6.4-6.4L6 13a6.5 6.5 0 009 9l5-5",
  codeBlock: "M4 5h16v14H4zM10 9l-2 3 2 3M14 9l2 3-2 3",
  rowBefore: "M4 8h16M4 14h16M4 20h16M12 2v4M10 4h4",
  rowAfter: "M4 4h16M4 10h16M4 16h16M12 18v4M10 20h4",
  colBefore: "M8 4v16M14 4v16M20 4v16M2 12h4M4 10v4",
  colAfter: "M4 4v16M10 4v16M16 4v16M18 12h4M20 10v4",
  deleteRow: "M4 8h16M4 16h16M9 12h6",
  deleteColumn: "M8 4v16M16 4v16M12 9v6",
  deleteTable: "M4 5h16v14H4zM8.5 9.5l7 5M15.5 9.5l-7 5",
  headerRow: "M4 5h16v5H4zM4 10h16v9H4M9.5 10v9M14.5 10v9",
  plus: "M12 5.5v13M5.5 12h13",
  chevronDown: "M6 9.5l6 6 6-6",
} as const;

type IconName = keyof typeof icons;

function Glyph({ name, className }: { name: IconName; className?: string }) {
  return (
    <svg
      aria-hidden="true"
      focusable="false"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      className={cn("size-4", className)}
    >
      <path d={icons[name]} />
    </svg>
  );
}

function ToolbarButton({
  icon,
  title,
  active,
  disabled,
  onClick,
}: {
  icon: IconName;
  title: string;
  active?: boolean;
  disabled?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      aria-pressed={active}
      disabled={disabled}
      className={cn(buttonClass, active && "bg-muted text-foreground")}
      // Without this the editor loses its selection the moment the button takes focus, and every
      // command would apply to nothing.
      onMouseDown={(event) => event.preventDefault()}
      onClick={onClick}
    >
      <Glyph name={icon} />
    </button>
  );
}

function Divider() {
  return <span className="mx-1 h-5 w-px shrink-0 bg-border" aria-hidden="true" />;
}

const BLOCK_TYPES = [
  { label: "Normal text", level: 0 },
  { label: "Heading 1", level: 1 },
  { label: "Heading 2", level: 2 },
  { label: "Heading 3", level: 3 },
  { label: "Heading 4", level: 4 },
  { label: "Heading 5", level: 5 },
  { label: "Heading 6", level: 6 },
] as const;

function DropdownMenu({
  label,
  title,
  icon,
  align = "left",
  children,
}: {
  label?: string;
  title: string;
  icon?: IconName;
  align?: "left" | "right";
  children: (close: () => void) => React.ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const ref = useDismissable(open, () => setOpen(false));

  return (
    <div ref={ref} className="relative shrink-0">
      <button
        type="button"
        title={title}
        aria-label={title}
        aria-haspopup="menu"
        aria-expanded={open}
        onMouseDown={(event) => event.preventDefault()}
        onClick={() => setOpen((current) => !current)}
        className={cn(
          "flex h-7 items-center gap-1 rounded px-2 text-sm font-medium text-foreground hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring",
          !label && "px-1.5 text-muted-foreground hover:text-foreground",
        )}
      >
        {icon ? <Glyph name={icon} /> : null}
        {label ? <span className="truncate">{label}</span> : null}
        <Glyph name="chevronDown" className="size-3" />
      </button>
      {open ? (
        <div
          role="menu"
          className={cn(
            "absolute z-30 mt-1 w-52 rounded-lg border border-border bg-card p-1 shadow-xl",
            align === "right" ? "right-0" : "left-0",
          )}
        >
          {children(() => setOpen(false))}
        </div>
      ) : null}
    </div>
  );
}

function MenuItem({ label, onSelect }: { label: string; onSelect: () => void }) {
  return (
    <button
      type="button"
      role="menuitem"
      onMouseDown={(event) => event.preventDefault()}
      onClick={onSelect}
      className="block w-full rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
    >
      {label}
    </button>
  );
}

/**
 * Table row/column operations, shown only while the cursor is inside a table. GitLab surfaces these in
 * a context menu; a contextual second row keeps them discoverable without a permanently crowded bar —
 * and without them an inserted table is a fixed 3×3 grid nobody can change.
 */
function TableControls({ editor }: { editor: Editor }) {
  if (!editor.isActive("table")) {
    return null;
  }

  return (
    <div className="flex flex-wrap items-center gap-0.5 border-t border-border px-2 py-1.5">
      <span className="mr-1 text-xs font-medium text-muted-foreground">Table</span>
      <ToolbarButton icon="rowBefore" title="Insert row above"
        onClick={() => editor.chain().focus().addRowBefore().run()} />
      <ToolbarButton icon="rowAfter" title="Insert row below"
        onClick={() => editor.chain().focus().addRowAfter().run()} />
      <ToolbarButton icon="deleteRow" title="Delete row"
        onClick={() => editor.chain().focus().deleteRow().run()} />
      <Divider />
      <ToolbarButton icon="colBefore" title="Insert column before"
        onClick={() => editor.chain().focus().addColumnBefore().run()} />
      <ToolbarButton icon="colAfter" title="Insert column after"
        onClick={() => editor.chain().focus().addColumnAfter().run()} />
      <ToolbarButton icon="deleteColumn" title="Delete column"
        onClick={() => editor.chain().focus().deleteColumn().run()} />
      <Divider />
      <ToolbarButton icon="headerRow" title="Toggle header row"
        onClick={() => editor.chain().focus().toggleHeaderRow().run()} />
      <ToolbarButton icon="deleteTable" title="Delete table"
        onClick={() => editor.chain().focus().deleteTable().run()} />
    </div>
  );
}

export function BasicToolbar({
  editor,
  className,
  onAttachFile,
}: {
  editor: Editor;
  className?: string;
  /** Wired to the caller's existing file-upload control; the paperclip is hidden when absent. */
  onAttachFile?: () => void;
}) {
  function promptForLink() {
    const previousUrl = editor.getAttributes("link").href as string | undefined;
    const url = window.prompt("Link URL", previousUrl ?? "");
    if (url === null) return;
    if (url === "") {
      editor.chain().focus().extendMarkRange("link").unsetLink().run();
      return;
    }
    editor.chain().focus().extendMarkRange("link").setLink({ href: url }).run();
  }

  function promptForImage() {
    const url = window.prompt("Image URL");
    if (!url) return;
    const alt = window.prompt("Description (for screen readers)") ?? "";
    editor.chain().focus().setImage({ src: url, alt }).run();
  }

  const activeBlock =
    BLOCK_TYPES.find((type) => type.level > 0 && editor.isActive("heading", { level: type.level })) ?? BLOCK_TYPES[0];

  return (
    <div className={className}>
      <div className="flex flex-wrap items-center gap-0.5 px-2 py-1.5">
        <ToolbarButton icon="undo" title="Undo" disabled={!editor.can().undo()}
          onClick={() => editor.chain().focus().undo().run()} />
        <ToolbarButton icon="redo" title="Redo" disabled={!editor.can().redo()}
          onClick={() => editor.chain().focus().redo().run()} />
        <Divider />

        <DropdownMenu label={activeBlock.label} title="Text style">
          {(close) =>
            BLOCK_TYPES.map((type) => (
              <MenuItem
                key={type.label}
                label={type.label}
                onSelect={() => {
                  close();
                  if (type.level === 0) editor.chain().focus().setParagraph().run();
                  else editor.chain().focus().toggleHeading({ level: type.level }).run();
                }}
              />
            ))
          }
        </DropdownMenu>
        <Divider />

        <ToolbarButton icon="bold" title="Bold" active={editor.isActive("bold")}
          onClick={() => editor.chain().focus().toggleBold().run()} />
        <ToolbarButton icon="italic" title="Italic" active={editor.isActive("italic")}
          onClick={() => editor.chain().focus().toggleItalic().run()} />
        <ToolbarButton icon="underline" title="Underline" active={editor.isActive("underline")}
          onClick={() => editor.chain().focus().toggleUnderline().run()} />
        <ToolbarButton icon="strike" title="Strikethrough" active={editor.isActive("strike")}
          onClick={() => editor.chain().focus().toggleStrike().run()} />
        <ToolbarButton icon="superscript" title="Superscript" active={editor.isActive("superscript")}
          onClick={() => editor.chain().focus().toggleSuperscript().run()} />
        <ToolbarButton icon="subscript" title="Subscript" active={editor.isActive("subscript")}
          onClick={() => editor.chain().focus().toggleSubscript().run()} />
        <ToolbarButton icon="clearFormat" title="Clear formatting"
          onClick={() => editor.chain().focus().unsetAllMarks().clearNodes().run()} />
        <Divider />

        <ToolbarButton icon="quote" title="Quote" active={editor.isActive("blockquote")}
          onClick={() => editor.chain().focus().toggleBlockquote().run()} />
        <ToolbarButton icon="code" title="Code" active={editor.isActive("code")}
          onClick={() => editor.chain().focus().toggleCode().run()} />
        <ToolbarButton icon="link" title="Link" active={editor.isActive("link")} onClick={promptForLink} />
        <Divider />

        <ToolbarButton icon="bulletList" title="Bullet list" active={editor.isActive("bulletList")}
          onClick={() => editor.chain().focus().toggleBulletList().run()} />
        <ToolbarButton icon="orderedList" title="Ordered list" active={editor.isActive("orderedList")}
          onClick={() => editor.chain().focus().toggleOrderedList().run()} />
        <ToolbarButton icon="taskList" title="Task list" active={editor.isActive("taskList")}
          onClick={() => editor.chain().focus().toggleTaskList().run()} />
        <ToolbarButton
          icon="outdent"
          title="Outdent"
          disabled={!editor.can().liftListItem("listItem") && !editor.can().liftListItem("taskItem")}
          onClick={() =>
            editor.chain().focus().liftListItem(editor.isActive("taskItem") ? "taskItem" : "listItem").run()
          }
        />
        <ToolbarButton
          icon="indent"
          title="Indent"
          disabled={!editor.can().sinkListItem("listItem") && !editor.can().sinkListItem("taskItem")}
          onClick={() =>
            editor.chain().focus().sinkListItem(editor.isActive("taskItem") ? "taskItem" : "listItem").run()
          }
        />
        <Divider />

        <ToolbarButton icon="table" title="Insert table" active={editor.isActive("table")}
          onClick={() => editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()} />
        <ToolbarButton icon="image" title="Insert image" onClick={promptForImage} />
        {onAttachFile ? <ToolbarButton icon="attach" title="Attach a file" onClick={onAttachFile} /> : null}
        <ToolbarButton icon="codeBlock" title="Code block" active={editor.isActive("codeBlock")}
          onClick={() => editor.chain().focus().toggleCodeBlock().run()} />
        <Divider />

        <DropdownMenu title="Insert" icon="plus" align="right">
          {(close) => {
            const insert = (run: () => void) => () => {
              close();
              run();
            };
            return (
              <>
                <MenuItem label="Code block" onSelect={insert(() => editor.chain().focus().toggleCodeBlock().run())} />
                <MenuItem label="Bullet list" onSelect={insert(() => editor.chain().focus().toggleBulletList().run())} />
                <MenuItem label="Ordered list" onSelect={insert(() => editor.chain().focus().toggleOrderedList().run())} />
                <MenuItem label="Task list" onSelect={insert(() => editor.chain().focus().toggleTaskList().run())} />
                <MenuItem
                  label="Horizontal rule"
                  onSelect={insert(() => editor.chain().focus().setHorizontalRule().run())}
                />
                <MenuItem
                  label="Table"
                  onSelect={insert(() =>
                    editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
                  )}
                />
                <MenuItem label="Image" onSelect={insert(promptForImage)} />
                <MenuItem
                  label="Mermaid diagram"
                  onSelect={insert(() =>
                    editor.chain().focus().insertContent("```mermaid\ngraph TD;\n  A-->B;\n```").run(),
                  )}
                />
                <MenuItem
                  label="PlantUML diagram"
                  onSelect={insert(() =>
                    editor
                      .chain()
                      .focus()
                      .insertContent("```plantuml\n@startuml\nAlice -> Bob: hello\n@enduml\n```")
                      .run(),
                  )}
                />
              </>
            );
          }}
        </DropdownMenu>
      </div>

      <TableControls editor={editor} />
    </div>
  );
}

export type EditorMode = "rich" | "plain" | "preview";

/**
 * Write/Preview tabs, the way GitLab and GitHub both do it. Preview is not decoration: markdown is
 * easy to get subtly wrong (a list that needs a blank line before it, an unclosed fence), and without
 * a preview the only way to find out is to post the comment.
 *
 * "Markdown" appears as a tab only while it is the active surface — the footer's "Switch to plain text
 * editing" link is the way in, matching GitLab, so the tab strip does not carry two controls for the
 * same thing.
 */
export function EditorTabs({
  mode,
  onSelect,
}: {
  mode: EditorMode;
  onSelect: (mode: EditorMode) => void;
}) {
  const tabs: { id: EditorMode; label: string }[] = [
    { id: mode === "plain" ? "plain" : "rich", label: mode === "plain" ? "Markdown" : "Write" },
    { id: "preview", label: "Preview" },
  ];

  return (
    <div role="tablist" className="flex items-center gap-1 border-b border-border px-2 pt-1.5">
      {tabs.map((tab) => {
        const selected = mode === tab.id;
        return (
          <button
            key={tab.label}
            type="button"
            role="tab"
            aria-selected={selected}
            onMouseDown={(event) => event.preventDefault()}
            onClick={() => onSelect(tab.id)}
            className={cn(
              "-mb-px rounded-t-md border-b-2 px-3 py-1.5 text-sm font-medium transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none",
              selected
                ? "border-primary text-foreground"
                : "border-transparent text-muted-foreground hover:text-foreground",
            )}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

/**
 * The footer strip under the editor: GitLab's plain-text escape hatch on the left and its markdown
 * hint on the right. The escape hatch matters — a rich-text surface with no way out is a trap when
 * someone needs to paste or fix raw markdown by hand.
 */
export function EditorFooter({
  plainText,
  onTogglePlainText,
}: {
  plainText: boolean;
  onTogglePlainText: () => void;
}) {
  return (
    <div className="flex items-center justify-between gap-3 border-t border-border px-3 py-1.5">
      <button
        type="button"
        onClick={onTogglePlainText}
        className="rounded text-xs font-medium text-primary underline-offset-4 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        {plainText ? "Switch to rich text editing" : "Switch to plain text editing"}
      </button>
      <span
        title="Markdown is supported"
        aria-label="Markdown is supported"
        className="select-none rounded border border-border px-1 text-[0.625rem] font-semibold leading-4 text-muted-foreground"
      >
        M↓
      </span>
    </div>
  );
}
