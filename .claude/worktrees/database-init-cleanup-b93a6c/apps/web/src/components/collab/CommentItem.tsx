"use client";

import { useState } from "react";
import { Button } from "@/components/ui/Button";
import type { AddCommentInput, Comment } from "@/lib/collab/types";
import { useCurrentUserId, useMemberDirectory } from "@/lib/members";
import { cn } from "@/lib/utils";
import { CommentComposer } from "./CommentComposer";

const reactionEmoji = ["👍", "✅", "👀", "🚀", "❤️"];

type CommentItemProps = {
  comment: Comment;
  onAddReply: (input: AddCommentInput) => Promise<void>;
  onEdit: (id: string, body: string) => Promise<void>;
  onDelete: (id: string) => Promise<void>;
  onToggleReaction: (comment: Comment, emoji: string) => Promise<void>;
};

function formatTimestamp(value: string) {
  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

export function CommentItem({
  comment,
  onAddReply,
  onEdit,
  onDelete,
  onToggleReaction,
}: CommentItemProps) {
  const [editing, setEditing] = useState(false);
  const [replying, setReplying] = useState(false);
  const [draft, setDraft] = useState(comment.body);
  const [busy, setBusy] = useState(false);
  const directory = useMemberDirectory();
  const currentUserId = useCurrentUserId();
  const isOwnComment = Boolean(currentUserId) && comment.authorUserId === currentUserId;
  const replies = comment.replies ?? [];
  const visibleReactions = reactionEmoji.map((emoji) => {
    const reaction = comment.reactions.find((item) => item.emoji === emoji);

    return {
      emoji,
      count: reaction?.userIds.length ?? 0,
      active: currentUserId ? (reaction?.userIds.includes(currentUserId) ?? false) : false,
    };
  });

  async function saveEdit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const body = draft.trim();

    if (!body) {
      return;
    }

    setBusy(true);

    try {
      await onEdit(comment.id, body);
      setEditing(false);
    } finally {
      setBusy(false);
    }
  }

  async function deleteOwnComment() {
    setBusy(true);

    try {
      await onDelete(comment.id);
    } finally {
      setBusy(false);
    }
  }

  return (
    <article className="space-y-3 rounded-xl border border-border bg-background p-3">
      <div className="flex items-start gap-3">
        <span className="grid size-9 shrink-0 place-items-center rounded-full bg-muted text-xs font-semibold">
          {directory.getInitials(comment.authorUserId)}
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <h4 className="text-sm font-semibold">{directory.getLabel(comment.authorUserId)}</h4>
            <time className="text-xs text-muted-foreground" dateTime={comment.createdAtUtc}>
              {formatTimestamp(comment.createdAtUtc)}
            </time>
            {comment.isEdited && !comment.isDeleted ? (
              <span className="text-xs text-muted-foreground">edited</span>
            ) : null}
          </div>

          {editing ? (
            <form className="mt-3 space-y-2" onSubmit={saveEdit}>
              <label className="sr-only" htmlFor={`edit-${comment.id}`}>
                Edit comment
              </label>
              <textarea
                id={`edit-${comment.id}`}
                value={draft}
                rows={3}
                className="w-full resize-y rounded-lg border border-border bg-card px-3 py-2 text-sm leading-6 outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                onChange={(event) => setDraft(event.currentTarget.value)}
              />
              <div className="flex justify-end gap-2">
                <Button type="button" variant="ghost" size="sm" onClick={() => setEditing(false)}>
                  Cancel
                </Button>
                <Button type="submit" size="sm" disabled={busy || !draft.trim()}>
                  Save
                </Button>
              </div>
            </form>
          ) : comment.isDeleted ? (
            <p className="mt-2 text-sm italic text-muted-foreground">Comment deleted</p>
          ) : (
            <p className="mt-2 whitespace-pre-wrap text-sm leading-6">{comment.body}</p>
          )}
        </div>
      </div>

      {!comment.isDeleted ? (
        <div className="flex flex-wrap items-center gap-2 pl-12">
          <div className="flex flex-wrap gap-1" aria-label="Comment reactions">
            {visibleReactions.map((reaction) => (
              <button
                key={reaction.emoji}
                type="button"
                aria-pressed={reaction.active}
                className={cn(
                  "rounded-full border border-border px-2 py-1 text-xs transition hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none",
                  reaction.active ? "bg-primary text-primary-foreground" : "bg-card",
                )}
                onClick={() => void onToggleReaction(comment, reaction.emoji)}
              >
                <span aria-hidden="true">{reaction.emoji}</span>
                <span className="sr-only">Toggle {reaction.emoji} reaction</span>
                {reaction.count > 0 ? <span aria-hidden="true"> {reaction.count}</span> : null}
              </button>
            ))}
          </div>

          {!comment.parentId ? (
            <Button type="button" variant="ghost" size="sm" onClick={() => setReplying(true)}>
              Reply
            </Button>
          ) : null}

          {isOwnComment ? (
            <>
              <Button type="button" variant="ghost" size="sm" onClick={() => setEditing(true)}>
                Edit
              </Button>
              <Button type="button" variant="ghost" size="sm" disabled={busy} onClick={deleteOwnComment}>
                Delete
              </Button>
            </>
          ) : null}
        </div>
      ) : null}

      {replying ? (
        <div className="pl-12">
          <CommentComposer
            taskId={comment.taskId}
            parentId={comment.id}
            autoFocus
            onCancel={() => setReplying(false)}
            onSubmit={async (input) => {
              await onAddReply(input);
              setReplying(false);
            }}
          />
        </div>
      ) : null}

      {replies.length > 0 ? (
        <ol className="space-y-3 border-l border-border pl-4 sm:ml-12">
          {replies.map((reply) => (
            <li key={reply.id}>
              <CommentItem
                comment={reply}
                onAddReply={onAddReply}
                onEdit={onEdit}
                onDelete={onDelete}
                onToggleReaction={onToggleReaction}
              />
            </li>
          ))}
        </ol>
      ) : null}
    </article>
  );
}
