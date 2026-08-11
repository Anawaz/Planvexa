"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button, buttonStyles } from "@/components/ui/Button";
import { addClipComment, clipDownloadHref, getClip, getClipTranscript, listClipComments, requestClipTranscript } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useMemberDirectory } from "@/lib/members";
import { useRecordRecentView } from "@/lib/recent/useRecordRecentView";
import { cn } from "@/lib/utils";
import { formatIsoDateTime, panelClassName, textInputClassName } from "./collab-ui";

function TranscriptPanel({ clipId }: { clipId: string }) {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const transcriptQuery = useQuery({
    queryKey: collabKeys.clipTranscript(workspaceId, clipId),
    queryFn: () => getClipTranscript(clipId),
  });
  const requestMutation = useMutation({
    mutationFn: () => requestClipTranscript(clipId),
    onSuccess: (transcript) => queryClient.setQueryData(collabKeys.clipTranscript(workspaceId, clipId), transcript),
  });

  const transcript = transcriptQuery.data;

  return (
    <div className={cn(panelClassName, "p-4")}>
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold">Transcript</h2>
        <Button type="button" size="sm" variant="outline" disabled={requestMutation.isPending} onClick={() => requestMutation.mutate()}>
          {transcript?.status === "Ready" ? "Re-transcribe" : "Transcribe"}
        </Button>
      </div>
      {requestMutation.isError ? (
        <p className="mt-2 text-xs text-red-700 dark:text-red-400">{(requestMutation.error as Error).message}</p>
      ) : null}
      <div className="mt-3 text-sm">
        {!transcript || transcript.status === "Unavailable" ? (
          <p className="text-xs text-muted-foreground">
            No transcription-capable AI provider is configured for this workspace (Settings → AI needs a
            Whisper-compatible <code>/audio/transcriptions</code> endpoint). This is an honest gap — never a
            fabricated transcript.
          </p>
        ) : transcript.status === "Pending" ? (
          <p className="text-xs text-muted-foreground">Transcribing…</p>
        ) : transcript.status === "Failed" ? (
          <p className="text-xs text-red-700 dark:text-red-400">Transcription failed. Try again.</p>
        ) : (
          <p className="whitespace-pre-wrap leading-6 text-foreground">{transcript.text}</p>
        )}
      </div>
    </div>
  );
}

function CommentsPanel({ clipId }: { clipId: string }) {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const directory = useMemberDirectory();
  const [body, setBody] = useState("");
  const commentsQuery = useQuery({
    queryKey: collabKeys.clipComments(workspaceId, clipId),
    queryFn: () => listClipComments(clipId),
  });
  const addMutation = useMutation({
    mutationFn: (text: string) => addClipComment(clipId, text),
    onSuccess: () => {
      setBody("");
      void queryClient.invalidateQueries({ queryKey: collabKeys.clipComments(workspaceId, clipId) });
    },
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!body.trim()) return;
    addMutation.mutate(body.trim());
  }

  return (
    <div className={cn(panelClassName, "p-4")}>
      <h2 className="text-sm font-semibold">Comments</h2>
      <div className="mt-3 space-y-3">
        {(commentsQuery.data ?? []).map((comment) => (
          <article key={comment.id} className="rounded-lg border border-border bg-card p-3">
            <p className="text-xs font-semibold">{directory.getLabel(comment.authorUserId)}</p>
            <p className="mt-1 text-xs text-muted-foreground">{formatIsoDateTime(comment.createdAtUtc)}</p>
            <p className="mt-2 text-sm leading-5">{comment.body}</p>
          </article>
        ))}
        {commentsQuery.data?.length === 0 ? <p className="text-sm text-muted-foreground">No comments yet.</p> : null}
      </div>
      <form onSubmit={submit} className="mt-3 flex gap-2">
        <input value={body} onChange={(event) => setBody(event.target.value)} className={cn(textInputClassName, "flex-1")} placeholder="Add a comment" />
        <Button type="submit" size="sm" disabled={addMutation.isPending}>
          Post
        </Button>
      </form>
    </div>
  );
}

export function ClipDetailPageClient({ clipId }: { clipId: string }) {
  useRecordRecentView("clip", clipId);
  const { workspaceId = "" } = useAppContext();
  const clipQuery = useQuery({
    queryKey: collabKeys.clip(workspaceId, clipId),
    queryFn: () => getClip(clipId),
  });
  const clip = clipQuery.data;

  if (clipQuery.isLoading) {
    return <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>Loading clip…</section>;
  }

  if (!clip) {
    return <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>Clip not found.</section>;
  }

  const isVideo = clip.contentType.startsWith("video/");
  const isAudio = clip.contentType.startsWith("audio/");
  const src = clipDownloadHref(clip.id);

  return (
    <section aria-labelledby="clip-detail-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Clips</p>
          <h1 id="clip-detail-title" className="mt-2 text-3xl font-semibold tracking-tight">
            {clip.title}
          </h1>
          {clip.description ? <p className="mt-2 max-w-2xl text-sm text-muted-foreground">{clip.description}</p> : null}
        </div>
        <Link href="/app/clips" className={buttonStyles({ variant: "outline", size: "sm" })}>
          Back to clips
        </Link>
      </div>

      <div className="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <div className={cn(panelClassName, "overflow-hidden p-4")}>
          {isVideo ? (
            <video src={src} controls className="w-full rounded-lg bg-black" />
          ) : isAudio ? (
            <audio src={src} controls className="w-full" />
          ) : (
            <a href={src} download className={buttonStyles({ variant: "outline", size: "sm" })}>
              Download ({clip.contentType})
            </a>
          )}
        </div>

        <aside className="space-y-4">
          <TranscriptPanel clipId={clip.id} />
          <CommentsPanel clipId={clip.id} />
        </aside>
      </div>
    </section>
  );
}
