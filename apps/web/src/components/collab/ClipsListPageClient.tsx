"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRef, useState } from "react";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { ResourcePicker } from "@/components/ui/ResourcePicker";
import { deleteClip, listClips, uploadClip } from "@/lib/collab/client";
import type { LinkedResourceType } from "@/lib/collab/types";
import { collabKeys } from "@/lib/collab/queries";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";
import type { SearchResultType } from "@/lib/search/client";
import { formatIsoDateTime, panelClassName, textInputClassName } from "./collab-ui";
import { useMediaRecorder, type RecordingKind } from "./clips/useMediaRecorder";

const fileSize = new Intl.NumberFormat("en", { style: "unit", unit: "megabyte", maximumFractionDigits: 1 });

export function ClipsListPageClient() {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [title, setTitle] = useState("");
  const [isPrivate, setIsPrivate] = useState(false);
  const [linkedResourceType, setLinkedResourceType] = useState<LinkedResourceType | "">("");
  const [linkedResourceId, setLinkedResourceId] = useState("");
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const recorder = useMediaRecorder();

  const clipsQuery = useQuery({ queryKey: collabKeys.clips(workspaceId), queryFn: listClips });
  const uploadMutation = useMutation({
    mutationFn: uploadClip,
    onSuccess: () => {
      setTitle("");
      setLinkedResourceType("");
      setLinkedResourceId("");
      void queryClient.invalidateQueries({ queryKey: collabKeys.clipsRoot(workspaceId) });
    },
  });
  const deleteMutation = useMutation({
    mutationFn: deleteClip,
    onSuccess: () => {
      setPendingDeleteId(null);
      void queryClient.invalidateQueries({ queryKey: collabKeys.clipsRoot(workspaceId) });
    },
  });
  const mutationError = uploadMutation.error ?? deleteMutation.error ?? recorder.error;

  function uploadFile(file: File, durationSeconds?: number) {
    uploadMutation.mutate({
      title: title.trim() || file.name || "Untitled clip",
      isPrivate,
      linkedResourceType: linkedResourceType || null,
      linkedResourceId: linkedResourceType ? linkedResourceId || null : null,
      file,
      fileName: file.name,
      durationSeconds,
    });
  }

  async function stopAndUpload() {
    const result = await recorder.stop();
    if (!result) return;
    uploadFile(new File([result.blob], `recording-${Date.now()}.webm`, { type: result.blob.type }), result.durationSeconds);
  }

  const clips = clipsQuery.data ?? [];

  return (
    <section aria-labelledby="clips-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Clips</p>
        <h1 id="clips-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Clips
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Record your screen, camera, or audio right in the browser, or upload a pre-recorded file. Clips get
          comments and (when a transcription-capable AI provider is configured) a searchable transcript.
        </p>
      </div>

      {mutationError ? (
        <p role="alert" className="rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300">
          {(mutationError as Error).message}
        </p>
      ) : null}

      <div className={cn(panelClassName, "p-4")}>
        <div className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
          <label className="grid gap-1 text-xs font-medium">
            Title
            <input
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              className="h-9 rounded-lg border border-border bg-background px-3 text-sm"
              placeholder="Standup recap"
            />
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={isPrivate}
              disabled={!!linkedResourceType}
              onChange={(event) => setIsPrivate(event.target.checked)}
              className="size-4 rounded border-border accent-[var(--primary)]"
            />
            Private to me
          </label>
        </div>

        <div className="mt-3 grid gap-3 sm:grid-cols-2">
          <label className="grid gap-1 text-xs font-medium">
            Link to Task/Document (optional)
            <select
              value={linkedResourceType}
              onChange={(event) => {
                setLinkedResourceType(event.target.value as LinkedResourceType | "");
                setLinkedResourceId("");
                if (event.target.value) setIsPrivate(false);
              }}
              className={textInputClassName}
            >
              <option value="">No link</option>
              <option value="task">Task</option>
              <option value="document">Document</option>
            </select>
          </label>
          {linkedResourceType ? (
            <ResourcePicker
              types={[(linkedResourceType === "task" ? "Task" : "Document") as SearchResultType]}
              value={linkedResourceId}
              onChange={(id) => setLinkedResourceId(id)}
              placeholder={`Search ${linkedResourceType}s…`}
            />
          ) : null}
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          {recorder.isRecording ? (
            <Button type="button" size="sm" variant="outline" className="border-red-300 text-red-700" onClick={() => void stopAndUpload()}>
              Stop recording
            </Button>
          ) : (
            (["screen", "camera", "audio"] as RecordingKind[]).map((kind) => (
              <Button key={kind} type="button" size="sm" variant="outline" onClick={() => void recorder.start(kind)}>
                Record {kind}
              </Button>
            ))
          )}
          <span className="mx-1 h-5 w-px bg-border" aria-hidden="true" />
          <Button type="button" size="sm" onClick={() => fileInputRef.current?.click()} disabled={uploadMutation.isPending}>
            Upload file
          </Button>
          <input
            ref={fileInputRef}
            type="file"
            accept="video/*,audio/*"
            className="hidden"
            onChange={(event) => {
              const file = event.target.files?.[0];
              if (file) uploadFile(file);
              event.target.value = "";
            }}
          />
          {uploadMutation.isPending ? <span className="text-xs text-muted-foreground">Uploading…</span> : null}
        </div>
      </div>

      <section className={cn(panelClassName, "overflow-hidden")} aria-labelledby="clip-list-title">
        <header className="border-b border-border p-4">
          <h2 id="clip-list-title" className="text-sm font-semibold">
            Clip library
          </h2>
        </header>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-4 py-3 font-semibold">Title</th>
                <th className="px-4 py-3 font-semibold">Visibility</th>
                <th className="px-4 py-3 font-semibold">Size</th>
                <th className="px-4 py-3 font-semibold">Status</th>
                <th className="px-4 py-3 font-semibold">Created</th>
                <th className="px-4 py-3 text-right font-semibold">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {clips.map((clip) => (
                <tr key={clip.id}>
                  <td className="px-4 py-3">
                    <Link href={`/app/clips/${clip.id}`} className="font-semibold text-foreground underline-offset-4 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
                      {clip.title}
                    </Link>
                    {clip.linkedResourceType ? (
                      <span className="ml-2 text-xs text-muted-foreground">Linked to {clip.linkedResourceType}</span>
                    ) : null}
                  </td>
                  <td className="px-4 py-3">
                    <span className={cn("rounded-full px-2.5 py-1 text-xs font-semibold", clip.isPrivate ? "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200" : "bg-primary/10 text-primary")}>
                      {clip.isPrivate ? "Private" : "Shared"}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">{fileSize.format(clip.sizeBytes / (1024 * 1024))}</td>
                  <td className="px-4 py-3 text-muted-foreground">{clip.status}</td>
                  <td className="px-4 py-3 text-muted-foreground">{formatIsoDateTime(clip.createdAtUtc)}</td>
                  <td className="px-4 py-3 text-right">
                    {pendingDeleteId === clip.id ? (
                      <span className="inline-flex flex-wrap items-center justify-end gap-2">
                        <span className="text-xs text-muted-foreground">Delete for everyone?</span>
                        <Button type="button" size="sm" variant="outline" className="border-red-300 text-red-700 dark:border-red-900 dark:text-red-400" disabled={deleteMutation.isPending} onClick={() => deleteMutation.mutate(clip.id)}>
                          Confirm
                        </Button>
                        <Button type="button" size="sm" variant="ghost" onClick={() => setPendingDeleteId(null)}>
                          Cancel
                        </Button>
                      </span>
                    ) : (
                      <Button type="button" size="sm" variant="ghost" className="text-red-600 hover:text-red-700 dark:text-red-400" aria-label={`Delete ${clip.title}`} onClick={() => setPendingDeleteId(clip.id)}>
                        Delete
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {clipsQuery.isLoading ? (
            <p className="p-4 text-sm text-muted-foreground">Loading clips…</p>
          ) : clips.length === 0 ? (
            <EmptyState className="m-4" title="No clips yet" description="Record your screen/camera/mic above, or upload a file." />
          ) : null}
        </div>
      </section>
    </section>
  );
}
