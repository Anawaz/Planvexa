"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { StatusBadge } from "@/components/admin/StatusBadge";
import { Button } from "@/components/ui/Button";
import { QueryState } from "@/components/ui/QueryState";
import {
  deleteHostWorkspace,
  getHostWorkspace,
  getHostWorkspaceUsage,
  restoreHostWorkspace,
  suspendHostWorkspace,
} from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";
import {
  ConfirmAction,
  IsoDateTime,
  MutationError,
  PageHeader,
  StatTile,
  formatBytes,
  panelClassName,
  tableHeaderClassName,
} from "./host-ui";

type PendingAction = "suspend" | "restore" | "delete" | null;

export function WorkspaceDetailPageClient({ workspaceId }: { workspaceId: string }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [pending, setPending] = useState<PendingAction>(null);

  const workspaceQuery = useQuery({
    queryKey: hostKeys.workspace(workspaceId),
    queryFn: () => getHostWorkspace(workspaceId),
  });

  // Its own query, because the API computes usage with a separate round trip into the target
  // workspace — the detail view should render as soon as the metadata arrives rather than waiting.
  const usageQuery = useQuery({
    queryKey: hostKeys.workspaceUsage(workspaceId),
    queryFn: () => getHostWorkspaceUsage(workspaceId),
  });

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: hostKeys.workspacesRoot() });
    void queryClient.invalidateQueries({ queryKey: hostKeys.overview() });
  }

  const suspendMutation = useMutation({
    mutationFn: () => suspendHostWorkspace(workspaceId),
    onSuccess: () => {
      setPending(null);
      invalidate();
    },
  });

  const restoreMutation = useMutation({
    mutationFn: () => restoreHostWorkspace(workspaceId),
    onSuccess: () => {
      setPending(null);
      invalidate();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (confirmSlug: string) => deleteHostWorkspace(workspaceId, confirmSlug),
    onSuccess: () => {
      invalidate();
      // The workspace no longer exists, so there is no detail page left to return to.
      router.replace("/host/workspaces");
    },
  });

  const detail = workspaceQuery.data;
  const suspended = detail?.summary.status === "Archived";

  return (
    <section aria-labelledby="host-workspace-title" className="space-y-6">
      <Link href="/host/workspaces" className="text-sm font-medium text-primary underline underline-offset-4">
        ← All workspaces
      </Link>

      <QueryState query={workspaceQuery} loadingLabel="Loading workspace…">
        {detail ? (
          <div className="space-y-6">
            <PageHeader
              id="host-workspace-title"
              eyebrow="Host administration"
              title={detail.summary.name}
              description={
                <>
                  <span className="font-mono">{detail.summary.slug}</span> · created{" "}
                  <IsoDateTime value={detail.summary.createdAtUtc} dateOnly /> · owner{" "}
                  {detail.summary.ownerDisplayName ?? "unknown"}
                  {detail.summary.ownerEmail ? ` (${detail.summary.ownerEmail})` : ""}
                </>
              }
            />

            <div className="flex flex-wrap items-center gap-3">
              <StatusBadge
                status={suspended ? "Suspended" : detail.summary.status}
                tone={suspended ? "red" : "green"}
              />
              <span className="text-sm text-muted-foreground">
                Last activity <IsoDateTime value={detail.summary.lastActivityAtUtc} fallback="never" />
              </span>
            </div>

            {/* Usage: counts and bytes only. Host administration is metadata-only by design — no task
                titles, document bodies or messages are available here or from the API. */}
            <QueryState query={usageQuery} loadingLabel="Loading usage…">
              {usageQuery.data ? (
                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                  <StatTile label="Members" value={detail.summary.memberCount} />
                  <StatTile label="Spaces" value={usageQuery.data.spaces} />
                  <StatTile label="Lists" value={usageQuery.data.lists} />
                  <StatTile label="Tasks" value={usageQuery.data.tasks} />
                  <StatTile label="Documents" value={usageQuery.data.documents} />
                  <StatTile
                    label="Attachments"
                    value={usageQuery.data.attachments}
                    hint={formatBytes(usageQuery.data.attachmentBytes)}
                  />
                </div>
              ) : null}
            </QueryState>

            <div className={panelClassName}>
              <h2 className="p-4 text-sm font-semibold">Members</h2>
              <div className="overflow-x-auto">
                <table className="w-full min-w-[40rem] text-left text-sm">
                  <thead className={tableHeaderClassName}>
                    <tr>
                      <th scope="col" className="px-4 py-2 font-semibold">Person</th>
                      <th scope="col" className="px-4 py-2 font-semibold">Role</th>
                      <th scope="col" className="px-4 py-2 font-semibold">Status</th>
                      <th scope="col" className="px-4 py-2 font-semibold">Joined</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detail.members.map((member) => (
                      <tr key={member.membershipId} className="border-t border-border">
                        <td className="px-4 py-2">
                          <Link
                            href={`/host/users/${member.userId}`}
                            className="font-medium text-primary underline underline-offset-4"
                          >
                            {member.displayName ?? "Unknown"}
                          </Link>
                          <p className="text-xs text-muted-foreground">{member.email}</p>
                        </td>
                        <td className="px-4 py-2">
                          {member.role}
                          {member.isGuest ? <span className="ml-2 text-xs text-muted-foreground">Guest</span> : null}
                        </td>
                        <td className="px-4 py-2">
                          <StatusBadge status={member.status} tone={member.status === "Active" ? "green" : "slate"} />
                        </td>
                        <td className="whitespace-nowrap px-4 py-2 text-muted-foreground">
                          <IsoDateTime value={member.joinedAtUtc} dateOnly />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>

            {detail.enabledFeatures.length > 0 ? (
              <div className={`${panelClassName} p-4`}>
                <h2 className="text-sm font-semibold">Enabled features</h2>
                <ul className="mt-3 flex flex-wrap gap-2">
                  {detail.enabledFeatures.map((feature) => (
                    <li key={feature} className="rounded-full bg-muted px-2.5 py-1 font-mono text-xs">
                      {feature}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}

            <div className={`${panelClassName} space-y-4 p-4`}>
              <div>
                <h2 className="text-sm font-semibold">Administrative actions</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  Suspending is reversible and destroys nothing — members simply cannot enter until it
                  is restored. Deleting is not reversible.
                </p>
              </div>

              {pending === null ? (
                <div className="flex flex-wrap gap-3">
                  {suspended ? (
                    <Button type="button" size="sm" variant="secondary" onClick={() => setPending("restore")}>
                      Restore workspace
                    </Button>
                  ) : (
                    <Button type="button" size="sm" variant="secondary" onClick={() => setPending("suspend")}>
                      Suspend workspace
                    </Button>
                  )}
                  <Button type="button" size="sm" variant="outline" onClick={() => setPending("delete")}>
                    Delete workspace
                  </Button>
                </div>
              ) : null}

              {pending === "suspend" ? (
                <ConfirmAction
                  title={`Suspend ${detail.summary.name}?`}
                  description={`All ${detail.summary.memberCount} member(s) lose access immediately. Nothing is deleted, and you can restore it at any time.`}
                  actionLabel="Suspend workspace"
                  pending={suspendMutation.isPending}
                  error={suspendMutation.error}
                  onConfirm={() => suspendMutation.mutate()}
                  onCancel={() => setPending(null)}
                />
              ) : null}

              {pending === "restore" ? (
                <ConfirmAction
                  title={`Restore ${detail.summary.name}?`}
                  description="Members regain access immediately."
                  actionLabel="Restore workspace"
                  pending={restoreMutation.isPending}
                  error={restoreMutation.error}
                  onConfirm={() => restoreMutation.mutate()}
                  onCancel={() => setPending(null)}
                />
              ) : null}

              {pending === "delete" ? (
                <ConfirmAction
                  title={`Permanently delete ${detail.summary.name}?`}
                  description="Every space, list, task, document, comment and attachment in this workspace is deleted. This cannot be undone."
                  actionLabel="Delete forever"
                  confirmText={detail.summary.slug}
                  pending={deleteMutation.isPending}
                  error={deleteMutation.error}
                  onConfirm={() => deleteMutation.mutate(detail.summary.slug)}
                  onCancel={() => setPending(null)}
                />
              ) : null}

              <MutationError error={suspendMutation.error ?? restoreMutation.error} />
            </div>
          </div>
        ) : null}
      </QueryState>
    </section>
  );
}
