"use client";

import { useRef, useState } from "react";
import { Button } from "@/components/ui/Button";
import { useAppContext } from "@/lib/app-context/AppContext";
import type { AddCommentInput } from "@/lib/collab/types";
import { useFileDropZone } from "@/lib/files/useFileDropZone";
import { useTypingBroadcast } from "@/lib/realtime/useRealtime";
import { BasicRichTextEditor } from "./tiptap/BasicRichTextEditor";

type CommentComposerProps = {
  taskId: string;
  parentId?: string;
  submitLabel?: string;
  placeholder?: string;
  autoFocus?: boolean;
  onCancel?: () => void;
  onSubmit: (input: AddCommentInput) => Promise<void>;
};

export function CommentComposer({
  taskId,
  parentId,
  submitLabel = parentId ? "Reply" : "Comment",
  placeholder = parentId ? "Write a reply…" : "Add a comment… (type @ to mention)",
  autoFocus = false,
  onCancel,
  onSubmit,
}: CommentComposerProps) {
  const [resetKey, setResetKey] = useState(0);
  const [body, setBody] = useState("");
  const [mentionUserIds, setMentionUserIds] = useState<string[]>([]);
  const [file, setFile] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const { workspaceId } = useAppContext();
  const broadcastTyping = useTypingBroadcast(workspaceId, "Task", taskId);
  const { isDraggingOver, dropZoneProps } = useFileDropZone((files) => {
    if (files[0]) setFile(files[0]);
  }, submitting);

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmedBody = body.trim();

    if (!trimmedBody) {
      setError("Write a comment before posting.");
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      await onSubmit({ taskId, parentId, body: trimmedBody, mentionUserIds, file });
      setBody("");
      setMentionUserIds([]);
      setFile(null);
      setResetKey((key) => key + 1);
    } catch {
      setError("Unable to save the comment. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form
      className={`space-y-3 rounded-lg border-2 border-dashed p-2 transition-colors ${
        isDraggingOver ? "border-primary bg-primary/5" : "border-transparent"
      }`}
      onSubmit={handleSubmit}
      {...dropZoneProps}
    >
      <div className="grid gap-2">
        <span className="text-sm font-medium">{parentId ? "Reply" : "New comment"}</span>
        <BasicRichTextEditor
          key={resetKey}
          ariaLabel={parentId ? "Reply" : "New comment"}
          value=""
          placeholder={placeholder}
          autoFocus={autoFocus}
          minHeightClassName={parentId ? "min-h-16" : "min-h-20"}
          // The toolbar's paperclip drives the same hidden file input as the "Attach a file" label
          // below, so there is one upload path rather than two competing ones.
          onAttachFile={() => fileInputRef.current?.click()}
          onChange={(markdown, mentions) => {
            setBody(markdown);
            setMentionUserIds(mentions);
            broadcastTyping();
          }}
        />
      </div>

      {error ? <p className="text-sm text-red-600 dark:text-red-400">{error}</p> : null}

      <div className="flex flex-wrap items-center justify-between gap-2">
        {file ? (
          <span className="flex items-center gap-2 text-xs text-muted-foreground">
            {file.name}
            <button
              type="button"
              aria-label="Remove attachment"
              className="rounded px-1 hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onClick={() => setFile(null)}
            >
              ×
            </button>
          </span>
        ) : (
          <label className="cursor-pointer text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline">
            Attach a file
            <input
              ref={fileInputRef}
              type="file"
              className="sr-only"
              onChange={(event) => setFile(event.currentTarget.files?.[0] ?? null)}
            />
          </label>
        )}

        <div className="flex justify-end gap-2">
          {onCancel ? (
            <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
              Cancel
            </Button>
          ) : null}
          <Button type="submit" size="sm" disabled={submitting}>
            {submitting ? "Posting…" : submitLabel}
          </Button>
        </div>
      </div>
    </form>
  );
}
