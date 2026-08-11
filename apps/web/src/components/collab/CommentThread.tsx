"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  addReaction,
  deleteComment,
  deleteCommentAttachment,
  editComment,
  listComments,
  removeReaction,
  uploadCommentAttachment,
} from "@/lib/collab/client";
import { addCommentOffline } from "@/lib/collab/offlineMutations";
import { collabKeys } from "@/lib/collab/queries";
import type { AddCommentInput, Comment } from "@/lib/collab/types";
import { useCurrentUserId } from "@/lib/members";
import { CommentComposer } from "./CommentComposer";
import { CommentItem } from "./CommentItem";
import { TypingIndicator } from "./TypingIndicator";

type CommentThreadProps = {
  taskId: string;
};

export function CommentThread({ taskId }: CommentThreadProps) {
  const queryClient = useQueryClient();
  const currentUserId = useCurrentUserId();
  const commentsKey = collabKeys.comments(taskId);
  const commentsQuery = useQuery({ queryKey: commentsKey, queryFn: () => listComments(taskId) });

  // ponytail: invalidate-on-success, no optimistic cache patching; the round trip is one request.
  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: commentsKey });
  };

  const addMutation = useMutation({
    mutationFn: async (input: AddCommentInput) => {
      const comment = await addCommentOffline(input, currentUserId ?? "");
      if (input.file) {
        // Best effort: the comment itself is already saved (or queued) at this point, so a failed
        // attachment upload (e.g. offline) shouldn't roll back or block the comment post — the
        // attachment is just silently skipped and the user can re-attach once back online.
        await uploadCommentAttachment(comment.id, input.file).catch(() => undefined);
      }

      return comment;
    },
    onSuccess: (comment) => {
      // Merge immediately: while offline there is no live refetch for `invalidate()` to resolve, so
      // the optimistic (or real) comment must land in the cache directly.
      queryClient.setQueryData<Comment[]>(commentsKey, (existing) => (existing ? [...existing, comment] : [comment]));
      invalidate();
    },
  });
  const deleteAttachmentMutation = useMutation({ mutationFn: deleteCommentAttachment, onSuccess: invalidate });
  const editMutation = useMutation({
    mutationFn: ({ id, body }: { id: string; body: string }) => editComment(id, body),
    onSuccess: invalidate,
  });
  const deleteMutation = useMutation({ mutationFn: deleteComment, onSuccess: invalidate });
  const reactionMutation = useMutation({
    mutationFn: ({ comment, emoji }: { comment: Comment; emoji: string }) => {
      const active = comment.reactions
        .find((reaction) => reaction.emoji === emoji)
        ?.userIds.includes(currentUserId ?? "");

      return active ? removeReaction(comment.id, emoji) : addReaction(comment.id, emoji);
    },
    onSuccess: invalidate,
  });

  const roots = commentsQuery.data ?? [];

  return (
    <section aria-labelledby="task-comments-title" className="space-y-4">
      <div>
        <h3 id="task-comments-title" className="text-sm font-semibold">
          Comments
        </h3>
        <p className="text-sm text-muted-foreground">
          Threaded discussion with mentions and reactions.
        </p>
      </div>

      <CommentComposer
        taskId={taskId}
        onSubmit={(input) => addMutation.mutateAsync(input).then(() => undefined)}
      />
      <TypingIndicator resourceType="Task" resourceId={taskId} />

      {commentsQuery.isLoading ? (
        <p className="text-sm text-muted-foreground">Loading comments…</p>
      ) : commentsQuery.isError ? (
        <p className="text-sm text-red-600 dark:text-red-400">Unable to load comments.</p>
      ) : roots.length === 0 ? (
        <p className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
          No comments yet. Start the conversation for this task.
        </p>
      ) : (
        <ol className="space-y-3">
          {roots.map((comment) => (
            <li key={comment.id}>
              <CommentItem
                comment={comment}
                onAddReply={(input) => addMutation.mutateAsync(input).then(() => undefined)}
                onEdit={(id, body) => editMutation.mutateAsync({ id, body }).then(() => undefined)}
                onDelete={(id) => deleteMutation.mutateAsync(id).then(() => undefined)}
                onDeleteAttachment={(id) => deleteAttachmentMutation.mutateAsync(id).then(() => undefined)}
                onToggleReaction={(item, emoji) =>
                  reactionMutation.mutateAsync({ comment: item, emoji }).then(() => undefined)
                }
              />
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
