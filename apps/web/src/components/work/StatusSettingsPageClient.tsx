"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import { PageHeader, panelClassName, selectClassName, textInputClassName } from "@/components/admin/admin-ui";
import { QueryState } from "@/components/ui/QueryState";
import { createStatusScheme, deleteStatusScheme, listWorkspaceStatusSchemes } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { statusPresets } from "@/lib/work/statusPresets";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";
import { StatusSchemeEditor } from "./StatusSchemeEditor";

// Same key prefix as the plain scheme list so any edit invalidates both, but a distinct leaf: this
// page asks for workspaceLevelOnly and must not overwrite the full list the board/list views read.
const workspaceSchemesKey = [...workKeys.statusSchemes(), "workspace-level"] as const;

export function StatusSettingsPageClient() {
  const queryClient = useQueryClient();
  const { currentWorkspace } = useAppContext();
  const [name, setName] = useState("");
  const [preset, setPreset] = useState("");

  // Mirrors WorkManagementAuthorizer.CanManageStructure (role >= Admin), which every status endpoint
  // enforces server-side. This only keeps a Member from clicking controls that would 403.
  const canManage = currentWorkspace?.role === "Admin" || currentWorkspace?.role === "Owner";

  const schemesQuery = useQuery({
    queryKey: workspaceSchemesKey,
    queryFn: listWorkspaceStatusSchemes,
  });
  const schemes = schemesQuery.data ?? [];

  const invalidate = () => void queryClient.invalidateQueries({ queryKey: workKeys.statusSchemes() });

  const createMutation = useMutation({
    mutationFn: () =>
      createStatusScheme(
        name.trim(),
        statusPresets.find((p) => p.name === preset)?.statuses ?? [{ name: "To Do", category: "NotStarted" }],
      ),
    onSuccess: () => {
      setName("");
      invalidate();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (schemeId: string) => deleteStatusScheme(schemeId),
    onSuccess: invalidate,
  });

  function create(event: FormEvent) {
    event.preventDefault();
    if (name.trim()) {
      createMutation.mutate();
    }
  }

  return (
    <section aria-labelledby="statuses-settings-title" className="space-y-6">
      <PageHeader
        id="statuses-settings-title"
        eyebrow="Settings"
        title="Statuses"
        description={
          <>
            These are the workspace default workflows. Every{" "}
            <Link href="/app/spaces" className="underline underline-offset-2">
              space
            </Link>{" "}
            uses the default until it overrides it with its own, from that space&rsquo;s
            &ldquo;Statuses &amp; workflow&rdquo; menu.
          </>
        }
      />

      <section className={cn(panelClassName, "p-4")} aria-labelledby="new-workflow-title">
        <h2 id="new-workflow-title" className="text-sm font-semibold">
          New workflow from a template
        </h2>
        <form onSubmit={create} className="mt-3 flex flex-wrap items-end gap-2">
          <label className="grid flex-1 gap-1 text-xs font-medium">
            Name
            <input
              value={name}
              placeholder="e.g. Engineering"
              onChange={(event) => setName(event.currentTarget.value)}
              className={cn(textInputClassName, "min-w-40")}
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Template
            <select value={preset} onChange={(event) => setPreset(event.currentTarget.value)} className={selectClassName}>
              <option value="">Blank (To Do only)</option>
              {statusPresets.map((p) => (
                <option key={p.name} value={p.name}>
                  {p.name}
                </option>
              ))}
            </select>
          </label>
          <Button type="submit" size="sm" disabled={!canManage || createMutation.isPending || name.trim() === ""}>
            Create workflow
          </Button>
        </form>
        {createMutation.isError ? (
          <p role="alert" className="mt-2 text-sm text-red-600 dark:text-red-400">
            {createMutation.error instanceof Error ? createMutation.error.message : "Could not create that workflow."}
          </p>
        ) : null}
      </section>

      {deleteMutation.isError ? (
        <p role="alert" className="text-sm text-red-600 dark:text-red-400">
          {deleteMutation.error instanceof Error ? deleteMutation.error.message : "Could not delete that workflow."}
        </p>
      ) : null}

      {/* Through QueryState so a failed load can never render as an empty page: this list used to be
          `data ?? []`, so an API failure showed "No workflows yet." — which reads as "this workspace
          has no workflows" rather than "this did not load", and hides every editor with it. */}
      <QueryState query={schemesQuery} loadingLabel="Loading workflows…">
        {schemes.length === 0 ? (
          <p className="rounded-lg border border-dashed border-border p-3 text-sm text-muted-foreground">
            This workspace has no workflows. Create one above.
          </p>
        ) : (
          schemes.map((scheme) => (
            <section key={scheme.id} className={panelClassName} aria-label={scheme.name}>
              <div className="flex items-center justify-end gap-2 px-4 pt-4">
                {scheme.isDefault ? (
                  <span className="mr-auto rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                    Default
                  </span>
                ) : null}
                <Button
                  variant="ghost"
                  size="sm"
                  className="text-red-600 dark:text-red-400"
                  disabled={scheme.isDefault || !canManage || deleteMutation.isPending}
                  title={scheme.isDefault ? "The default workflow cannot be deleted." : undefined}
                  onClick={() => {
                    if (window.confirm(`Delete the workflow "${scheme.name}"? Lists still using it will block this.`)) {
                      deleteMutation.mutate(scheme.id);
                    }
                  }}
                >
                  Delete workflow
                </Button>
              </div>
              <StatusSchemeEditor scheme={scheme} canManage={canManage} onChanged={invalidate} />
            </section>
          ))
        )}
      </QueryState>
    </section>
  );
}
