"use client";

import Link from "next/link";
import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { StatusBadge } from "@/components/admin/StatusBadge";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { QueryState } from "@/components/ui/QueryState";
import { getHostUser, setHostUserActive, setHostUserHostAdmin } from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";
import {
  ConfirmAction,
  IsoDateTime,
  MutationError,
  PageHeader,
  panelClassName,
  tableHeaderClassName,
} from "./host-ui";

type PendingAction = "disable" | "revoke-host-admin" | null;

export function UserDetailPageClient({ userId }: { userId: string }) {
  const queryClient = useQueryClient();
  const [pending, setPending] = useState<PendingAction>(null);

  const userQuery = useQuery({ queryKey: hostKeys.user(userId), queryFn: () => getHostUser(userId) });

  function invalidate() {
    setPending(null);
    void queryClient.invalidateQueries({ queryKey: hostKeys.user(userId) });
    void queryClient.invalidateQueries({ queryKey: hostKeys.usersRoot() });
    void queryClient.invalidateQueries({ queryKey: hostKeys.overview() });
    // The caller may have just demoted THEMSELVES out of the console (the API refuses, but a grant to
    // someone else changes what the shell should offer), so the status probe is stale either way.
    void queryClient.invalidateQueries({ queryKey: hostKeys.status() });
  }

  const activeMutation = useMutation({
    mutationFn: (active: boolean) => setHostUserActive(userId, active),
    onSuccess: invalidate,
  });

  const hostAdminMutation = useMutation({
    mutationFn: (granted: boolean) => setHostUserHostAdmin(userId, granted),
    onSuccess: invalidate,
  });

  const detail = userQuery.data;
  const user = detail?.summary;

  return (
    <section aria-labelledby="host-user-title" className="space-y-6">
      <Link href="/host/users" className="text-sm font-medium text-primary underline underline-offset-4">
        ← All accounts
      </Link>

      <QueryState query={userQuery} loadingLabel="Loading account…">
        {detail && user ? (
          <div className="space-y-6">
            <PageHeader
              id="host-user-title"
              eyebrow="Host administration"
              title={user.displayName}
              description={
                <>
                  {user.email} · registered <IsoDateTime value={user.createdAtUtc} dateOnly /> · last seen{" "}
                  <IsoDateTime value={user.lastSeenAtUtc} fallback="never" />
                </>
              }
            />

            <div className="flex flex-wrap items-center gap-2">
              <StatusBadge status={user.isActive ? "Active" : "Disabled"} tone={user.isActive ? "green" : "red"} />
              {user.isHostAdmin ? <StatusBadge status="Host administrator" tone="blue" /> : null}
              {user.isAnonymized ? <StatusBadge status="Deleted (anonymized)" tone="slate" /> : null}
            </div>

            <div className={panelClassName}>
              <h2 className="p-4 text-sm font-semibold">Workspace memberships</h2>
              {detail.memberships.length === 0 ? (
                <div className="p-4 pt-0">
                  <EmptyState
                    title="No memberships"
                    description="This account does not belong to any workspace on this server."
                  />
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full min-w-[40rem] text-left text-sm">
                    <thead className={tableHeaderClassName}>
                      <tr>
                        <th scope="col" className="px-4 py-2 font-semibold">Workspace</th>
                        <th scope="col" className="px-4 py-2 font-semibold">Role</th>
                        <th scope="col" className="px-4 py-2 font-semibold">Membership</th>
                        <th scope="col" className="px-4 py-2 font-semibold">Joined</th>
                      </tr>
                    </thead>
                    <tbody>
                      {detail.memberships.map((membership) => (
                        <tr key={membership.workspaceId} className="border-t border-border">
                          <td className="px-4 py-2">
                            <Link
                              href={`/host/workspaces/${membership.workspaceId}`}
                              className="font-medium text-primary underline underline-offset-4"
                            >
                              {membership.workspaceName}
                            </Link>
                            {membership.workspaceStatus === "Archived" ? (
                              <span className="ml-2 text-xs text-muted-foreground">(suspended)</span>
                            ) : null}
                          </td>
                          <td className="px-4 py-2">{membership.role}</td>
                          <td className="px-4 py-2">
                            <StatusBadge
                              status={membership.status}
                              tone={membership.status === "Active" ? "green" : "slate"}
                            />
                          </td>
                          <td className="whitespace-nowrap px-4 py-2 text-muted-foreground">
                            <IsoDateTime value={membership.joinedAtUtc} dateOnly />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            <div className={`${panelClassName} space-y-4 p-4`}>
              <div>
                <h2 className="text-sm font-semibold">Account actions</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  Disabling blocks sign-in across the whole server and is fully reversible. Host
                  administration grants access to this console — the server refuses to leave the
                  installation with no active host administrator, and refuses to let you disable or
                  demote yourself.
                </p>
              </div>

              {pending === null ? (
                <div className="flex flex-wrap gap-3">
                  {user.isActive ? (
                    <Button type="button" size="sm" variant="secondary" onClick={() => setPending("disable")}>
                      Disable account
                    </Button>
                  ) : (
                    <Button
                      type="button"
                      size="sm"
                      variant="secondary"
                      disabled={user.isAnonymized || activeMutation.isPending}
                      onClick={() => activeMutation.mutate(true)}
                    >
                      Enable account
                    </Button>
                  )}

                  {user.isHostAdmin ? (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => setPending("revoke-host-admin")}
                    >
                      Revoke host administration
                    </Button>
                  ) : (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      disabled={!user.isActive || hostAdminMutation.isPending}
                      onClick={() => hostAdminMutation.mutate(true)}
                    >
                      Make host administrator
                    </Button>
                  )}
                </div>
              ) : null}

              {user.isAnonymized ? (
                <p className="text-sm text-muted-foreground">
                  This account was deleted under the account-deletion flow. Its personal data has been
                  scrubbed, so it cannot be re-enabled.
                </p>
              ) : null}

              {pending === "disable" ? (
                <ConfirmAction
                  title={`Disable ${user.displayName}?`}
                  description="They are signed out of every workspace on their next request. Their data and memberships are untouched, and you can enable the account again at any time."
                  actionLabel="Disable account"
                  pending={activeMutation.isPending}
                  error={activeMutation.error}
                  onConfirm={() => activeMutation.mutate(false)}
                  onCancel={() => setPending(null)}
                />
              ) : null}

              {pending === "revoke-host-admin" ? (
                <ConfirmAction
                  title={`Revoke host administration from ${user.displayName}?`}
                  description="They lose access to this console on their next request. Their workspace memberships and roles are unaffected."
                  actionLabel="Revoke host administration"
                  pending={hostAdminMutation.isPending}
                  error={hostAdminMutation.error}
                  onConfirm={() => hostAdminMutation.mutate(false)}
                  onCancel={() => setPending(null)}
                />
              ) : null}

              <MutationError error={activeMutation.error ?? hostAdminMutation.error} />
            </div>
          </div>
        ) : null}
      </QueryState>
    </section>
  );
}
