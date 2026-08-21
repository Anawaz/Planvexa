"use client";

import { EditorContent, useEditor } from "@tiptap/react";
import { cn } from "@/lib/utils";
import { createEditorExtensions } from "./editorExtensions";

/**
 * Read-only render of a markdown string, for the editor's Preview tab.
 *
 * Mounts the same extension set the editor uses, which is what makes the preview honest: this is
 * exactly how the saved content will look wherever it is displayed (CommentItem renders comments the
 * same way). A separate markdown renderer would drift from the editor's own schema and show something
 * the reader will never actually see.
 *
 * Its own component rather than a second `useEditor` inside the editor, so the instance is created
 * only while the preview is open instead of on every mount of every comment in a thread.
 */
export function MarkdownPreview({
  markdown,
  className,
  minHeightClassName,
}: {
  markdown: string;
  className?: string;
  minHeightClassName?: string;
}) {
  const editor = useEditor({
    extensions: createEditorExtensions(),
    content: markdown,
    editable: false,
    immediatelyRender: false,
    editorProps: {
      attributes: {
        class: cn("prose prose-sm dark:prose-invert max-w-none text-sm leading-6 outline-none", minHeightClassName),
      },
    },
  });

  if (!editor) {
    return null;
  }

  return (
    <div className={className}>
      {markdown.trim() ? (
        <EditorContent editor={editor} />
      ) : (
        <p className={cn("text-sm text-muted-foreground", minHeightClassName)}>Nothing to preview yet.</p>
      )}
    </div>
  );
}
