"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { FormEvent } from "react";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { createExport, exportDownloadHref, listExports } from "@/lib/admin/client";
import { adminKeys } from "@/lib/admin/queries";
import type { ExportDataset } from "@/lib/admin/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";
import {
  IsoDateTime,
  numberFormatter,
  PageHeader,
  panelClassName,
  selectClassName,
  tableHeaderClassName,
} from "./admin-ui";
import { StatusBadge } from "./StatusBadge";

const datasetOptions: Array<{ value: ExportDataset; label: string; help: string }> = [
  { value: "audit", label: "Audit log", help: "Governance events and actor metadata" },
  { value: "tasks", label: "Tasks", help: "Task data for workspace retention workflows" },
];

function datasetLabel(dataset: string) {
  return datasetOptions.find((option) => option.value === dataset)?.label ?? dataset;
}

export function ExportsPageClient() {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [dataset, setDataset] = useState<ExportDataset>("audit");
  const [statusMessage, setStatusMessage] = useState("");
  const exportsQuery = useQuery({ queryKey: adminKeys.exports(workspaceId), queryFn: listExports });
  const createMutation = useMutation({
    mutationFn: createExport,
    onSuccess: (job) => {
      setStatusMessage(`Queued ${datasetLabel(job.dataset)} export ${job.id}.`);
      void queryClient.invalidateQueries({ queryKey: adminKeys.exportsRoot(workspaceId) });
      void queryClient.invalidateQueries({ queryKey: adminKeys.auditRoot(workspaceId) });
    },
  });
  const jobs = exportsQuery.data ?? [];

  function submitExport(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    createMutation.mutate(dataset);
  }

  return (
    <section aria-labelledby="exports-title" className="space-y-6">
      <PageHeader
        id="exports-title"
        eyebrow="Governed exports"
        title="Exports"
        description="Create governed workspace data exports and download completed jobs."
      />

      {statusMessage ? (
        <p role="status" className="rounded-lg bg-primary/10 px-4 py-3 text-sm font-medium text-primary">
          {statusMessage}
        </p>
      ) : null}

      <form onSubmit={submitExport} className={cn(panelClassName, "grid gap-4 p-5 md:grid-cols-[1fr_auto]")}>
        <label htmlFor="export-dataset" className="grid gap-2 text-sm font-medium">
          New export
          <select
            id="export-dataset"
            value={dataset}
            onChange={(event) => setDataset(event.target.value as ExportDataset)}
            className={selectClassName}
          >
            {datasetOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          <span className="text-xs text-muted-foreground">
            {datasetOptions.find((option) => option.value === dataset)?.help}
          </span>
        </label>
        <Button type="submit" className="self-end" disabled={createMutation.isPending}>
          Create export
        </Button>
      </form>

      <section className={cn(panelClassName, "overflow-hidden")} aria-labelledby="export-jobs-title">
        <div className="border-b border-border p-5">
          <h2 id="export-jobs-title" className="text-lg font-semibold">
            Export jobs
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Completed jobs expose workspace-scoped download links.
          </p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[56rem] text-left text-sm">
            <caption className="sr-only">Governed export jobs.</caption>
            <thead className={tableHeaderClassName}>
              <tr>
                <th className="px-4 py-3 font-semibold">Dataset</th>
                <th className="px-4 py-3 font-semibold">Status</th>
                <th className="px-4 py-3 font-semibold">Created</th>
                <th className="px-4 py-3 font-semibold">Completed</th>
                <th className="px-4 py-3 font-semibold">Rows</th>
                <th className="px-4 py-3 font-semibold">Download</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {jobs.map((job) => (
                <tr key={job.id}>
                  <td className="px-4 py-3 font-medium">{datasetLabel(job.dataset)}</td>
                  <td className="px-4 py-3">
                    <StatusBadge status={job.status} />
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    <IsoDateTime value={job.createdAtUtc} />
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">
                    <IsoDateTime value={job.completedAtUtc} fallback="Not completed" />
                  </td>
                  <td className="px-4 py-3">{job.rowCount === null || job.rowCount === undefined ? "—" : numberFormatter.format(job.rowCount)}</td>
                  <td className="px-4 py-3">
                    {job.status === "Completed" ? (
                      <a
                        href={exportDownloadHref(job.id)}
                        className="text-sm font-semibold text-primary underline-offset-4 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                      >
                        Download
                      </a>
                    ) : (
                      <span className="text-muted-foreground">Unavailable</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {exportsQuery.isLoading ? <p className="p-4 text-sm text-muted-foreground">Loading export jobs…</p> : null}
        </div>
      </section>
    </section>
  );
}
