/** Offline-aware drop-in replacement for `addComment` — same shape as
 * `work/offlineMutations.ts`'s `createTaskOffline`; see its doc comment for the general pattern. */
import { getApiContext } from "@/lib/api-client";
import { queueOrRun } from "@/lib/offline/withOfflineFallback";
import { addComment as addCommentRequest } from "./client";
import type { AddCommentInput, Comment } from "./types";

export async function addCommentOffline(input: AddCommentInput, currentUserId: string): Promise<Comment> {
  const workspaceId = getApiContext().workspaceId;
  const { result } = await queueOrRun<Comment>({
    workspaceId,
    type: "comment.create",
    payload: input as unknown as Record<string, unknown>,
    onlineCall: (idempotencyKey) => addCommentRequest(input, { idempotencyKey }),
    buildOptimistic: (localId) => ({
      id: localId,
      taskId: input.taskId,
      parentId: input.parentId ?? null,
      authorUserId: currentUserId,
      body: input.body,
      isEdited: false,
      isDeleted: false,
      mentionUserIds: input.mentionUserIds ?? [],
      reactions: [],
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: null,
      replies: [],
      attachments: [],
    }),
  });
  return result;
}
