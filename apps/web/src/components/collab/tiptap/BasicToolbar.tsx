"use client";

import type { Editor } from "@tiptap/react";
import { cn } from "@/lib/utils";

const buttonClass =
  "rounded px-1.5 py-1 text-xs font-medium text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:opacity-40";

function ToolbarButton({
  label,
  title,
  active,
  onClick,
}: {
  label: string;
  title: string;
  active?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      aria-pressed={active}
      className={cn(buttonClass, active && "bg-muted text-foreground")}
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
    >
      {label}
    </button>
  );
}

/** Minimal formatting bar for task descriptions and comments: bold/italic/strikethrough, lists, and a
 * link. Headings/quotes/code blocks stay unused here — GitLab's own comment/description editor keeps
 * the same restrained toolbar for these fields. */
export function BasicToolbar({ editor, className }: { editor: Editor; className?: string }) {
  return (
    <div className={cn("flex flex-wrap items-center gap-0.5", className)}>
      <ToolbarButton
        label="B"
        title="Bold"
        active={editor.isActive("bold")}
        onClick={() => editor.chain().focus().toggleBold().run()}
      />
      <ToolbarButton
        label="I"
        title="Italic"
        active={editor.isActive("italic")}
        onClick={() => editor.chain().focus().toggleItalic().run()}
      />
      <ToolbarButton
        label="S"
        title="Strikethrough"
        active={editor.isActive("strike")}
        onClick={() => editor.chain().focus().toggleStrike().run()}
      />
      <span className="mx-1 h-4 w-px bg-border" />
      <ToolbarButton
        label="•"
        title="Bullet list"
        active={editor.isActive("bulletList")}
        onClick={() => editor.chain().focus().toggleBulletList().run()}
      />
      <ToolbarButton
        label="1."
        title="Numbered list"
        active={editor.isActive("orderedList")}
        onClick={() => editor.chain().focus().toggleOrderedList().run()}
      />
      <span className="mx-1 h-4 w-px bg-border" />
      <ToolbarButton
        label="🔗"
        title="Link"
        active={editor.isActive("link")}
        onClick={() => {
          const previousUrl = editor.getAttributes("link").href as string | undefined;
          const url = window.prompt("Link URL", previousUrl ?? "");
          if (url === null) return;
          if (url === "") {
            editor.chain().focus().extendMarkRange("link").unsetLink().run();
            return;
          }
          editor.chain().focus().extendMarkRange("link").setLink({ href: url }).run();
        }}
      />
    </div>
  );
}
