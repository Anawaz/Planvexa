"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import {
  commitImportJob,
  getImportJob,
  listImportJobRows,
  listImportJobs,
  listImportSources,
  setImportMapping,
  uploadImportJob,
  validateImportJob,
} from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";
import { formatIsoDateTime, panelClassName, textInputClassName } from "./collab-ui";

// ImportTargetFields on the API (src/Modules/WorkManagement/.../Application/Importers/ImportSource.cs).
const targetFields = [
  "Title",
  "Description",
  "StatusName",
  "PriorityName",
  "DueDate",
  "Tags",
  "SpaceName",
  "ListName",
  "Done",
];

export function ImportsPageClient() {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null);
  const [sourceType, setSourceType] = useState("Csv");
  const [targetSpaceName, setTargetSpaceName] = useState("");
  const [targetListName, setTargetListName] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [mapping, setMapping] = useState<Record<string, string>>({});

  const sourcesQuery = useQuery({ queryKey: collabKeys.importSources(), queryFn: listImportSources });
  const jobsQuery = useQuery({ queryKey: collabKeys.importJobs(workspaceId), queryFn: listImportJobs });
  const jobs = jobsQuery.data ?? [];
  const activeJobId = selectedJobId ?? jobs[0]?.id ?? "";

  const jobQuery = useQuery({
    queryKey: collabKeys.importJob(workspaceId, activeJobId),
    queryFn: () => getImportJob(activeJobId),
    enabled: Boolean(activeJobId),
  });
  const rowsQuery = useQuery({
    queryKey: collabKeys.importJobRows(workspaceId, activeJobId),
    queryFn: () => listImportJobRows(activeJobId),
    enabled: Boolean(activeJobId),
  });

  const uploadMutation = useMutation({
    mutationFn: uploadImportJob,
    onSuccess: (job) => {
      setSelectedJobId(job.id);
      setFile(null);
      setMapping(job.columnMappingJson ? (JSON.parse(job.columnMappingJson) as Record<string, string>) : {});
      void queryClient.invalidateQueries({ queryKey: collabKeys.importJobsRoot(workspaceId) });
    },
  });
  const uploadError = uploadMutation.error;
  const mappingMutation = useMutation({
    mutationFn: ({ id, mapping: m }: { id: string; mapping: Record<string, string> }) => setImportMapping(id, m),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: collabKeys.importJob(workspaceId, activeJobId) }),
  });
  const validateMutation = useMutation({
    mutationFn: validateImportJob,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: collabKeys.importJob(workspaceId, activeJobId) });
      void queryClient.invalidateQueries({ queryKey: collabKeys.importJobRows(workspaceId, activeJobId) });
    },
  });
  const commitMutation = useMutation({
    mutationFn: commitImportJob,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: collabKeys.importJob(workspaceId, activeJobId) });
      void queryClient.invalidateQueries({ queryKey: collabKeys.importJobRows(workspaceId, activeJobId) });
      void queryClient.invalidateQueries({ queryKey: collabKeys.importJobsRoot(workspaceId) });
    },
  });

  function submitUpload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!file) {
      return;
    }

    uploadMutation.mutate({
      sourceType,
      file,
      targetSpaceName: targetSpaceName.trim() || undefined,
      targetListName: targetListName.trim() || undefined,
    });
  }

  const job = jobQuery.data;
  const rows = rowsQuery.data ?? [];
  const sources = sourcesQuery.data ?? [];

  return (
    <section aria-labelledby="imports-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Importers</p>
        <h1 id="imports-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Data importers
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Upload a CSV/Excel sheet or a Trello board export, map columns to task fields, validate, then
          commit — resumable if interrupted, and every row lands as a real task through the normal
          authorized creation path.
        </p>
      </div>

      <section className={cn(panelClassName, "p-4")} aria-labelledby="upload-title">
        <h2 id="upload-title" className="text-lg font-semibold">
          Upload a source file
        </h2>
        {uploadError ? (
          <p
            role="alert"
            className="mt-4 rounded-[var(--radius)] border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
          >
            This file could not be uploaded: {(uploadError as Error).message}
          </p>
        ) : null}
        <form onSubmit={submitUpload} className="mt-4 grid gap-4 lg:grid-cols-[10rem_1fr_1fr_auto]">
          <label className="grid gap-1 text-xs font-medium">
            Source type
            <select value={sourceType} onChange={(event) => setSourceType(event.target.value)} className={textInputClassName}>
              {(sources.length > 0 ? sources : ["Csv", "Xlsx", "Trello"]).map((type) => (
                <option key={type} value={type}>
                  {type}
                </option>
              ))}
            </select>
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Default target Space (used when a row has no SpaceName)
            <input value={targetSpaceName} onChange={(event) => setTargetSpaceName(event.target.value)} className={textInputClassName} placeholder="Imported" />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Default target List
            <input value={targetListName} onChange={(event) => setTargetListName(event.target.value)} className={textInputClassName} placeholder="Imported" />
          </label>
          <Button type="submit" size="sm" className="self-end" disabled={!file || uploadMutation.isPending}>
            Upload
          </Button>
          <label className="lg:col-span-4 grid gap-1 text-xs font-medium">
            File
            <input
              type="file"
              accept=".csv,.xlsx,.json"
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
              className={textInputClassName}
            />
          </label>
        </form>
      </section>

      <section className="grid gap-6 xl:grid-cols-[20rem_1fr]">
        <div className="space-y-2" aria-label="Import jobs">
          {jobsQuery.isLoading ? (
            <p className="text-sm text-muted-foreground">Loading import jobs…</p>
          ) : (
            jobs.map((j) => (
              <button
                key={j.id}
                type="button"
                aria-pressed={activeJobId === j.id}
                onClick={() => setSelectedJobId(j.id)}
                className="w-full rounded-xl border border-border bg-card p-3 text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              >
                <span className="block text-sm font-semibold">{j.fileName}</span>
                <span className="mt-1 block text-xs text-muted-foreground">
                  {j.sourceType} · {j.status} · {j.committedRows}/{j.totalRows} committed
                </span>
              </button>
            ))
          )}
        </div>

        <section className={cn(panelClassName, "p-4")} aria-labelledby="job-detail-title">
          {!job ? (
            <p className="text-sm text-muted-foreground">Upload a file, or select a job on the left.</p>
          ) : (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border pb-4">
                <div>
                  <h2 id="job-detail-title" className="text-lg font-semibold">
                    {job.fileName}
                  </h2>
                  <p className="text-xs text-muted-foreground">
                    {job.status} · {job.totalRows} rows · {job.errorCount} errors · created {formatIsoDateTime(job.createdAtUtc)}
                  </p>
                </div>
                <div className="flex gap-2">
                  <Button type="button" variant="secondary" size="sm" disabled={validateMutation.isPending} onClick={() => validateMutation.mutate(job.id)}>
                    Validate
                  </Button>
                  <Button type="button" size="sm" disabled={commitMutation.isPending} onClick={() => commitMutation.mutate(job.id)}>
                    Commit
                  </Button>
                </div>
              </div>

              {job.detectedColumns.length > 0 ? (
                <div>
                  <h3 className="text-sm font-semibold">Column mapping</h3>
                  <p className="mt-1 text-xs text-muted-foreground">
                    Map each task field to a source column, then Validate. A Trello import is pre-mapped
                    automatically.
                  </p>
                  <div className="mt-3 grid gap-2 sm:grid-cols-2">
                    {targetFields.map((field) => (
                      <label key={field} className="grid gap-1 text-xs font-medium">
                        {field}
                        <select
                          value={mapping[field] ?? ""}
                          onChange={(event) => setMapping((current) => ({ ...current, [field]: event.target.value }))}
                          className={textInputClassName}
                        >
                          <option value="">— not mapped —</option>
                          {job.detectedColumns.map((column) => (
                            <option key={column} value={column}>
                              {column}
                            </option>
                          ))}
                        </select>
                      </label>
                    ))}
                  </div>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    className="mt-3"
                    disabled={mappingMutation.isPending}
                    onClick={() => mappingMutation.mutate({ id: job.id, mapping })}
                  >
                    Save mapping
                  </Button>
                </div>
              ) : null}

              <div className="overflow-x-auto rounded-xl border border-border">
                <table className="w-full min-w-[44rem] text-left text-sm">
                  <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
                    <tr>
                      <th className="px-4 py-3 font-semibold">Row</th>
                      <th className="px-4 py-3 font-semibold">Status</th>
                      <th className="px-4 py-3 font-semibold">Error</th>
                      <th className="px-4 py-3 font-semibold">Task</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border">
                    {rows.map((row) => (
                      <tr key={row.id}>
                        <td className="px-4 py-3">{row.rowIndex + 1}</td>
                        <td className="px-4 py-3">
                          <span
                            className={cn(
                              "rounded-full px-2.5 py-1 text-xs font-semibold",
                              row.status === "Committed" && "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200",
                              row.status === "Invalid" && "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
                            )}
                          >
                            {row.status}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-muted-foreground">{row.errorMessage ?? "—"}</td>
                        <td className="px-4 py-3 text-muted-foreground">{row.createdTaskId ?? "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {rowsQuery.isLoading ? <p className="p-4 text-sm text-muted-foreground">Loading rows…</p> : null}
              </div>
            </div>
          )}
        </section>
      </section>
    </section>
  );
}
