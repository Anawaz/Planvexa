"use client";

import { useEffect, useId, useMemo, useRef, useState } from "react";
import { Button } from "@/components/ui/Button";
import { useAppContext } from "@/lib/app-context/AppContext";
import type { AddCommentInput } from "@/lib/collab/types";
import { useCurrentUserId, useMemberDirectory, useMembers } from "@/lib/members";
import { useTypingBroadcast } from "@/lib/realtime/useRealtime";

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
  placeholder = parentId ? "Write a reply…" : "Add a comment…",
  autoFocus = false,
  onCancel,
  onSubmit,
}: CommentComposerProps) {
  const bodyId = useId();
  const menuId = useId();
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const [body, setBody] = useState("");
  const [mentionUserIds, setMentionUserIds] = useState<string[]>([]);
  const [mentionOpen, setMentionOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const { data: members } = useMembers();
  const directory = useMemberDirectory();
  const currentUserId = useCurrentUserId();
  const { workspaceId } = useAppContext();
  const broadcastTyping = useTypingBroadcast(workspaceId, "Task", taskId);
  const mentionableMembers = useMemo(
    () =>
      (members ?? [])
        .filter((member) => member.userId !== currentUserId)
        .map((member) => ({
          id: member.userId,
          name: directory.getLabel(member.userId),
          initials: directory.getInitials(member.userId),
        })),
    [members, currentUserId, directory],
  );

  useEffect(() => {
    if (autoFocus) {
      textareaRef.current?.focus();
    }
  }, [autoFocus]);

  useEffect(() => {
    if (!mentionOpen) {
      return;
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        setMentionOpen(false);
        textareaRef.current?.focus();
      }
    }

    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [mentionOpen]);

  function toggleMention(userId: string) {
    const member = mentionableMembers.find((item) => item.id === userId);

    setMentionUserIds((current) =>
      current.includes(userId)
        ? current.filter((id) => id !== userId)
        : [...current, userId],
    );

    if (member && !body.includes(`@${member.name}`)) {
      setBody((current) => `${current}${current.trim() ? " " : ""}@${member.name} `);
    }
  }

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmedBody = body.trim();

    if (!trimmedBody) {
      setError("Write a comment before posting.");
      textareaRef.current?.focus();
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      await onSubmit({ taskId, parentId, body: trimmedBody, mentionUserIds });
      setBody("");
      setMentionUserIds([]);
      setMentionOpen(false);
    } catch {
      setError("Unable to save the comment. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className="space-y-3" onSubmit={handleSubmit}>
      <div className="grid gap-2">
        <label htmlFor={bodyId} className="text-sm font-medium">
          {parentId ? "Reply" : "New comment"}
        </label>
        <textarea
          id={bodyId}
          ref={textareaRef}
          value={body}
          rows={parentId ? 3 : 4}
          placeholder={placeholder}
          className="min-h-24 resize-y rounded-lg border border-border bg-background px-3 py-2 text-sm leading-6 outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          onChange={(event) => {
            setBody(event.currentTarget.value);
            broadcastTyping();
          }}
        />
      </div>

      <div className="relative flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-haspopup="listbox"
          aria-expanded={mentionOpen}
          aria-controls={menuId}
          onClick={() => setMentionOpen((open) => !open)}
        >
          @ Mention
        </Button>
        {mentionUserIds.map((userId) => {
          const member = mentionableMembers.find((item) => item.id === userId);

          return (
            <span
              key={userId}
              className="rounded-full bg-muted px-2 py-1 text-xs text-muted-foreground"
            >
              @{member?.name ?? userId}
            </span>
          );
        })}
        {mentionOpen ? (
          <div
            id={menuId}
            role="listbox"
            aria-label="Mention teammates"
            aria-multiselectable="true"
            className="absolute left-0 top-11 z-20 w-64 rounded-xl border border-border bg-card p-2 text-sm shadow-xl"
          >
            {mentionableMembers.map((member) => {
              const selected = mentionUserIds.includes(member.id);

              return (
                <button
                  key={member.id}
                  type="button"
                  role="option"
                  aria-selected={selected}
                  className="flex w-full items-center gap-2 rounded-lg px-2 py-2 text-left hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
                  onClick={() => toggleMention(member.id)}
                >
                  <span className="grid size-7 place-items-center rounded-full bg-muted text-xs font-semibold">
                    {member.initials}
                  </span>
                  <span className="flex-1">{member.name}</span>
                  <span className="text-xs text-muted-foreground">{selected ? "Selected" : "Add"}</span>
                </button>
              );
            })}
          </div>
        ) : null}
      </div>

      {error ? <p className="text-sm text-red-600 dark:text-red-400">{error}</p> : null}

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
    </form>
  );
}
