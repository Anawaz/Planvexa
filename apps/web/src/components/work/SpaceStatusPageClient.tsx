"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import { PageHeader, panelClassName, selectClassName } from "@/components/admin/admin-ui";
import {
  customizeSpaceStatusScheme,
  getSpaceStatusScheme,
  listSpaces,
  listWorkspaceStatusSchemes,
  resetSpaceStatusScheme,
} from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { statusPresets } from "@/lib/work/statusPresets";
import { useAppContext } from "@/lib/app-context/AppContext";
import type { StatusDefinition, StatusScheme } from "@/lib/work/types";
import { cn } from "@/lib/utils";
import { StatusSchemeEditor } from "./StatusSchemeEditor";
import { useFocusTrap } from "./useFocusTrap";

/**
 * Reverting a Space to the workspace default needs somewhere for its tasks to land: the API rejects
 * the revert until every status that still holds tasks has a target in the workspace default scheme.
 */
function RevertToDefaultDialog({
  spaceId,
  scheme,
  defaultScheme,
  onClose,
  onDone,
}: {
  spaceId: string;
  scheme: StatusScheme;
  defaultScheme?: StatusScheme;
  onClose: () => void;
  onDone: () => void;
}) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const targets = defaultScheme?.statuses ?? [];
  // Only the user's explicit overrides. The name-matched prefill is derived at render instead of
  // frozen into state: the workspace-default query is gated on the dialog opening, so at first
  // render `targets` is still empty and a useState initializer would bake in "no match" for
  // everything — leaving every row on "Leave unmapped" and the revert rejected by the API.
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const targetFor = (status: StatusDefinition) =>
    overrides[status.id] ??
    targets.find((target) => target.name.toLowerCase() === status.name.toLowerCase())?.id ??
    "";

  useFocusTrap({ open: true, containerRef: dialogRef, onClose });

  const mutation = useMutation({
    mutationFn: () =>
      resetSpaceStatusScheme(
        spaceId,
        scheme.statuses
          .map((status) => ({ fromStatusId: status.id, toStatusId: targetFor(status) }))
          .filter((entry) => entry.toStatusId !== ""),
      ),
    onSuccess: () => {
      onDone();
      onClose();
    },
  });

  return (
    <div className="fixed inset-0 z-[60]" role="presentation">
      <button
        type="button"
        aria-label="Cancel reverting to the workspace default"
        className="absolute inset-0 cursor-default bg-slate-950/50 backdrop-blur-[1px]"
        onClick={onClose}
      />
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="revert-statuses-title"
        tabIndex={-1}
        className="absolute left-1/2 top-1/2 w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-border bg-card p-5 shadow-2xl outline-none"
      >
        <h3 id="revert-statuses-title" className="text-lg font-semibold">
          Use the workspace default
        </h3>
        <p className="mt-1 text-sm text-muted-foreground">
          This space&rsquo;s own workflow goes away. Choose where the tasks on each of its statuses land in
          &ldquo;{defaultScheme?.name ?? "the workspace default"}&rdquo;.
        </p>

        <form
          className="mt-4 grid gap-3"
          onSubmit={(event: FormEvent) => {
            event.preventDefault();
            mutation.mutate();
          }}
        >
          {scheme.statuses.map((status) => (
            <label key={status.id} className="grid grid-cols-2 items-center gap-2 text-sm">
              <span className="truncate">{status.name}</span>
              <select
                aria-label={`Replacement for ${status.name}`}
                value={targetFor(status)}
                disabled={mutation.isPending}
                onChange={(event) => {
                  const value = event.currentTarget.value;
                  setOverrides((current) => ({ ...current, [status.id]: value }));
                }}
                className={selectClassName}
              >
                <option value="">Leave unmapped</option>
                {targets.map((target) => (
                  <option key={target.id} value={target.id}>
                    {target.name}
                  </option>
                ))}
              </select>
            </label>
          ))}

          {mutation.isError ? (
            <p role="alert" className="text-sm text-red-600 dark:text-red-400">
              {mutation.error instanceof Error ? mutation.error.message : "Could not revert this space."}
            </p>
          ) : null}

          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" size="sm" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" size="sm" disabled={mutation.isPending || targets.length === 0}>
              Use workspace default
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}

export function SpaceStatusPageClient({ spaceId }: { spaceId: string }) {
  const queryClient = useQueryClient();
  const { currentWorkspace } = useAppContext();
  const canManage = currentWorkspace?.role === "Admin" || currentWorkspace?.role === "Owner";
  const [reverting, setReverting] = useState(false);
  const [preset, setPreset] = useState(statusPresets[0].name);

  const spacesQuery = useQuery({ queryKey: workKeys.spaces(), queryFn: listSpaces });
  const spaceName = spacesQuery.data?.find((space) => space.id === spaceId)?.name ?? "Space";

  const schemeQuery = useQuery({
    queryKey: workKeys.spaceStatusScheme(spaceId),
    queryFn: () => getSpaceStatusScheme(spaceId),
  });

  // Only the revert dialog uses this (as the list of possible targets), but it is fetched with the
  // page rather than on open: gating it on `reverting` meant the dialog's first paint had no target
  // options at all and a disabled submit button.
  const defaultSchemeQuery = useQuery({
    queryKey: [...workKeys.statusSchemes(), "workspace-level"],
    queryFn: listWorkspaceStatusSchemes,
  });

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: workKeys.spaceStatusScheme(spaceId) });
    void queryClient.invalidateQueries({ queryKey: workKeys.statusSchemes() });
  };

  const customizeMutation = useMutation({
    mutationFn: (presetName?: string) =>
      customizeSpaceStatusScheme(spaceId, statusPresets.find((p) => p.name === presetName)?.statuses),
    onSuccess: invalidate,
  });

  if (schemeQuery.isLoading) {
    return <p className="text-sm text-muted-foreground">Loading statuses…</p>;
  }

  if (!schemeQuery.data) {
    return (
      <p role="alert" className="text-sm text-red-600 dark:text-red-400">
        {schemeQuery.error instanceof Error ? schemeQuery.error.message : "Could not load this space's statuses."}
      </p>
    );
  }

  const { scheme, isCustomized } = schemeQuery.data;
  // Two independent conditions: `isCustomized` says the Space HAS its own scheme (editing an
  // inherited one would change every other Space), `canManage` says this user MAY edit it — mirrors
  // WorkManagementAuthorizer.CanManageStructure (role >= Admin), enforced server-side regardless.
  const canEditScheme = isCustomized && canManage;

  return (
    <section aria-labelledby="space-statuses-title" className="space-y-6">
      <PageHeader
        id="space-statuses-title"
        eyebrow={spaceName}
        title="Statuses & workflow"
        description={
          <>
            The statuses tasks in this space move through. Workspace defaults live in{" "}
            <Link href="/app/settings/statuses" className="underline underline-offset-2">
              Settings → Statuses
            </Link>
            .
          </>
        }
      />

      <div className={cn(panelClassName, "p-4")}>
        {isCustomized ? (
          <>
            <p className="text-sm font-semibold">Custom to this space.</p>
            <p className="mt-1 text-sm text-muted-foreground">
              Changes here affect only {spaceName}.
            </p>
            <Button
              variant="secondary"
              size="sm"
              className="mt-3"
              disabled={!canManage}
              onClick={() => setReverting(true)}
            >
              Use workspace default
            </Button>
          </>
        ) : (
          <>
            <p className="text-sm font-semibold">This space uses the workspace default.</p>
            <p className="mt-1 text-sm text-muted-foreground">
              Editing here would change every space that inherits it. Give this space its own copy first.
            </p>
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <Button size="sm" disabled={!canManage || customizeMutation.isPending} onClick={() => customizeMutation.mutate(undefined)}>
                Customize this space
              </Button>
              <select
                aria-label="Template"
                value={preset}
                onChange={(event) => setPreset(event.currentTarget.value)}
                className={selectClassName}
              >
                {statusPresets.map((p) => (
                  <option key={p.name} value={p.name}>
                    {p.name}
                  </option>
                ))}
              </select>
              <Button
                variant="secondary"
                size="sm"
                disabled={!canManage || customizeMutation.isPending}
                onClick={() => {
                  const template = statusPresets.find((p) => p.name === preset);
                  if (
                    template &&
                    window.confirm(
                      `Replace this space's statuses with the "${preset}" template? Every task in ${spaceName} will be moved to "${template.statuses[0].name}".`,
                    )
                  ) {
                    customizeMutation.mutate(preset);
                  }
                }}
              >
                Customize from a template
              </Button>
            </div>
          </>
        )}
        {customizeMutation.isError ? (
          <p role="alert" className="mt-2 text-sm text-red-600 dark:text-red-400">
            {customizeMutation.error instanceof Error ? customizeMutation.error.message : "Could not customize this space."}
          </p>
        ) : null}
      </div>

      <section className={panelClassName} aria-label={scheme.name}>
        <StatusSchemeEditor scheme={scheme} canManage={canEditScheme} onChanged={invalidate} />
      </section>

      {reverting ? (
        <RevertToDefaultDialog
          spaceId={spaceId}
          scheme={scheme}
          defaultScheme={defaultSchemeQuery.data?.find((s) => s.isDefault)}
          onClose={() => setReverting(false)}
          onDone={invalidate}
        />
      ) : null}
    </section>
  );
}
