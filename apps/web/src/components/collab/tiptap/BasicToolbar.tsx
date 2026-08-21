"use client";

import type { Editor } from "@tiptap/react";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

/**
 * The formatting bar for task descriptions and comments, modelled on GitLab's Content Editor (the same
 * Tiptap/ProseMirror engine, so the same controls are available).
 *
 * Every control here has to survive a round trip through markdown — the content is stored as a
 * markdown string in the existing description/comment-body columns, and it is RENDERED by mounting
 * this same editor read-only (see CommentItem). A control is therefore only worth having if
 * tiptap-markdown can serialize it and Tiptap can parse it back, which is what
 * BasicRichTextEditor.roundtrip.test.tsx pins down.
 *
 * Four of GitLab's items are absent because each needs a custom Tiptap node (and, for two of them, a
 * renderer) rather than a button — shipping them as raw insertions loses the content on save:
 *
 * - "Alert" (`> [!NOTE]`): the serializer escapes the marker to `\[!NOTE\]` and folds the line break,
 *   so it degrades to an ordinary quote. Needs its own node with a raw-writing serializer.
 * - "Collapsible section": `<details>` is HTML, which this editor deliberately does not parse
 *   (`html: false`), so it would be stripped on the next edit.
 * - "Embedded view" is GitLab-specific (its own metrics/chart embeds) and has no Planvexa analogue.
 * - "Table of contents" is GitLab's `[[_TOC_]]`, which nothing in Planvexa renders.
 *
 * Underline is absent for the reason GitLab omits it too: markdown cannot express it, so it would be
 * silently dropped on save. Mermaid and PlantUML insert fenced code blocks with the right language
 * tag — exactly what GitLab stores — and display as code until a diagram renderer exists.
 */

const buttonClass =
  "grid size-7 shrink-0 place-items-center rounded text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-40 motion-reduce:transition-none";

/** One 24-grid stroke path per glyph, matching the app's own icon set (app-shell/icons.tsx). */
const icons = {
  bold: "M7 5h6a3.5 3.5 0 010 7H7zM7 12h7a3.5 3.5 0 010 7H7z",
  italic: "M14 5h-4M14 5l-4 14M10 19H6",
  strike: "M5 12h14M8 8.5A3 3 0 0111 6h2a3 3 0 012.5 1.4M16 15a3 3 0 01-3 2.5h-2A3 3 0 018 15.5",
  quote: "M5 6h14M5 12h9M5 18h9M18 11v8",
  code: "M9 8l-4 4 4 4M15 8l4 4-4 4",
  link: "M10 13a4 4 0 006 .5l2-2a4 4 0 00-5.7-5.7l-1 1M14 11a4 4 0 00-6-.5l-2 2a4 4 0 005.7 5.7l1-1",
  bulletList: "M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01",
  orderedList: "M10 6h10M10 12h10M10 18h10M4 5h1v4M4 9h2M4 13h2v2H4v2h2",
  taskList: "M10 6h10M10 12h10M10 18h10M3.5 6l1.2 1.2L7 5M3.5 12l1.2 1.2L7 11M3.5 18l1.2 1.2L7 17",
  table: "M4 5h16v14H4zM4 10h16M4 15h16M9.5 5v14M14.5 5v14",
  attach: "M17 8l-6.5 6.5a2.5 2.5 0 003.5 3.5L21 11a4.5 4.5 0 00-6.4-6.4L6 13a6.5 6.5 0 009 9l5-5",
  codeBlock: "M4 5h16v14H4zM10 9l-2 3 2 3M14 9l2 3-2 3",
  horizontalRule: "M4 12h16",
  plus: "M12 5.5v13M5.5 12h13",
  chevronDown: "M6 9.5l6 6 6-6",
} as const;

function Glyph({ name, className }: { name: keyof typeof icons; className?: string }) {
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
  icon: keyof typeof icons;
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

/** Closes on outside click and Escape — the two ways anyone expects a menu to go away. */
function useDismissable(open: boolean, close: () => void) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: MouseEvent) {
      if (ref.current && !ref.current.contains(event.target as Node)) close();
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") close();
    }

    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open, close]);

  return ref;
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

function BlockTypeMenu({ editor }: { editor: Editor }) {
  const [open, setOpen] = useState(false);
  const ref = useDismissable(open, () => setOpen(false));

  const active = BLOCK_TYPES.find((type) => type.level > 0 && editor.isActive("heading", { level: type.level }))
    ?? BLOCK_TYPES[0];

  return (
    <div ref={ref} className="relative shrink-0">
      <button
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        onMouseDown={(event) => event.preventDefault()}
        onClick={() => setOpen((current) => !current)}
        className="flex h-7 items-center gap-1 rounded px-2 text-sm font-medium text-foreground hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        <span className="truncate">{active.label}</span>
        <Glyph name="chevronDown" className="size-3.5" />
      </button>
      {open ? (
        <div
          role="menu"
          className="absolute left-0 z-30 mt-1 w-44 rounded-lg border border-border bg-card p-1 shadow-xl"
        >
          {BLOCK_TYPES.map((type) => (
            <button
              key={type.label}
              type="button"
              role="menuitem"
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => {
                setOpen(false);
                if (type.level === 0) {
                  editor.chain().focus().setParagraph().run();
                } else {
                  editor.chain().focus().toggleHeading({ level: type.level }).run();
                }
              }}
              className={cn(
                "block w-full rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring",
                active.label === type.label && "bg-muted font-medium",
              )}
            >
              {type.label}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

function InsertMenu({ editor }: { editor: Editor }) {
  const [open, setOpen] = useState(false);
  const ref = useDismissable(open, () => setOpen(false));

  // GitLab's "+" menu, minus the two items that cannot round-trip into anything Planvexa renders —
  // see this file's header comment.
  const items: { label: string; run: () => void }[] = [
    { label: "Code block", run: () => editor.chain().focus().toggleCodeBlock().run() },
    { label: "Bullet list", run: () => editor.chain().focus().toggleBulletList().run() },
    { label: "Ordered list", run: () => editor.chain().focus().toggleOrderedList().run() },
    { label: "Task list", run: () => editor.chain().focus().toggleTaskList().run() },
    { label: "Horizontal rule", run: () => editor.chain().focus().setHorizontalRule().run() },
    { label: "Table", run: () => editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run() },
    {
      // Stored exactly as GitLab stores it: a fenced block tagged `mermaid`. Renders as code here
      // until a diagram renderer exists, and the source is never lost.
      label: "Mermaid diagram",
      run: () => editor.chain().focus().insertContent("```mermaid\ngraph TD;\n  A-->B;\n```").run(),
    },
    {
      label: "PlantUML diagram",
      run: () => editor.chain().focus().insertContent("```plantuml\n@startuml\nAlice -> Bob: hello\n@enduml\n```").run(),
    },
  ];

  return (
    <div ref={ref} className="relative shrink-0">
      <button
        type="button"
        title="Insert"
        aria-label="Insert"
        aria-haspopup="menu"
        aria-expanded={open}
        onMouseDown={(event) => event.preventDefault()}
        onClick={() => setOpen((current) => !current)}
        className={cn("flex h-7 items-center gap-0.5 rounded px-1.5", buttonClass, "size-auto")}
      >
        <Glyph name="plus" />
        <Glyph name="chevronDown" className="size-3" />
      </button>
      {open ? (
        <div
          role="menu"
          className="absolute right-0 z-30 mt-1 w-52 rounded-lg border border-border bg-card p-1 shadow-xl"
        >
          {items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => {
                setOpen(false);
                item.run();
              }}
              className="block w-full rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
            >
              {item.label}
            </button>
          ))}
        </div>
      ) : null}
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
  /** Wired to the caller's existing file-upload input; the paperclip is hidden when absent. */
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

  return (
    <div className={cn("flex flex-wrap items-center gap-0.5", className)}>
      <BlockTypeMenu editor={editor} />
      <Divider />

      <ToolbarButton icon="bold" title="Bold" active={editor.isActive("bold")}
        onClick={() => editor.chain().focus().toggleBold().run()} />
      <ToolbarButton icon="italic" title="Italic" active={editor.isActive("italic")}
        onClick={() => editor.chain().focus().toggleItalic().run()} />
      <ToolbarButton icon="strike" title="Strikethrough" active={editor.isActive("strike")}
        onClick={() => editor.chain().focus().toggleStrike().run()} />
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
      <Divider />

      <ToolbarButton
        icon="table"
        title="Insert table"
        active={editor.isActive("table")}
        onClick={() => editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run()}
      />
      {onAttachFile ? <ToolbarButton icon="attach" title="Attach a file" onClick={onAttachFile} /> : null}
      <ToolbarButton icon="codeBlock" title="Code block" active={editor.isActive("codeBlock")}
        onClick={() => editor.chain().focus().toggleCodeBlock().run()} />
      <Divider />

      <InsertMenu editor={editor} />
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
