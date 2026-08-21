"use client";

import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { ApiError, apiClient } from "@/lib/api-client";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useCurrentUserId, useMembers, usePendingInvitations, type Member } from "@/lib/members";
import { TeamsPanel } from "@/components/people/TeamsPanel";

const roles = ["Owner", "Admin", "Member", "Guest"] as const;

function formatDate(value?: string) {
  return value ? new Date(value).toLocaleDateString() : "—";
}

function errorMessage(error: unknown, fallback: string) {
  return error instanceof ApiError ? error.message : fallback;
}

export default function MembersPage() {
  const { workspaceId } = useAppContext();
  const currentUserId = useCurrentUserId();
  const queryClient = useQueryClient();
  const membersQuery = useMembers();
  const invitationsQuery = usePendingInvitations();
  const [inviteOpen, setInviteOpen] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  function invalidateMembers() {
    queryClient.invalidateQueries({ queryKey: ["members", workspaceId] });
  }
  function invalidateInvitations() {
    queryClient.invalidateQueries({ queryKey: ["invitations", workspaceId] });
  }

  const invite = useMutation({
    mutationFn: (body: { email: string; role: string }) =>
      apiClient.post(`/workspaces/${workspaceId}/invitations`, body),
    onSuccess: () => {
      invalidateInvitations();
      setInviteOpen(false);
    },
  });

  const resend = useMutation({
    mutationFn: (invitationId: string) =>
      apiClient.post(`/workspaces/${workspaceId}/invitations/${invitationId}/resend`),
    onSuccess: invalidateInvitations,
    onError: (error) => setActionError(errorMessage(error, "Could not resend invitation.")),
  });

  const revoke = useMutation({
    mutationFn: (invitationId: string) =>
      apiClient.post(`/workspaces/${workspaceId}/invitations/${invitationId}/revoke`),
    onSuccess: invalidateInvitations,
    onError: (error) => setActionError(errorMessage(error, "Could not revoke invitation.")),
  });

  const changeRole = useMutation({
    mutationFn: (vars: { membershipId: string; role: string }) =>
      apiClient.patch(`/workspaces/${workspaceId}/members/${vars.membershipId}`, { role: vars.role }),
    onSuccess: invalidateMembers,
    onError: (error) => setActionError(errorMessage(error, "Could not change role.")),
  });

  const setActive = useMutation({
    mutationFn: (vars: { membershipId: string; activate: boolean }) =>
      apiClient.post(
        `/workspaces/${workspaceId}/members/${vars.membershipId}/${vars.activate ? "reactivate" : "deactivate"}`,
      ),
    onSuccess: invalidateMembers,
    onError: (error) => setActionError(errorMessage(error, "Could not update member.")),
  });

  const transfer = useMutation({
    mutationFn: (membershipId: string) =>
      apiClient.post(`/workspaces/${workspaceId}/transfer-ownership`, { membershipId }),
    onSuccess: invalidateMembers,
    onError: (error) => setActionError(errorMessage(error, "Could not transfer ownership.")),
  });

  const leave = useMutation({
    mutationFn: () => apiClient.post(`/workspaces/${workspaceId}/leave`),
    onError: (error) => setActionError(errorMessage(error, "Could not leave workspace.")),
    onSuccess: () => window.location.assign("/app"),
  });

  function handleInvite(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setActionError(null);
    const form = new FormData(event.currentTarget);
    invite.mutate({ email: String(form.get("email")), role: String(form.get("role")) });
  }

  function handleTransfer(member: Member) {
    if (window.confirm(`Transfer ownership to ${member.displayName ?? member.email ?? "this member"}? You will become an Admin.`)) {
      setActionError(null);
      transfer.mutate(member.id);
    }
  }

  function handleLeave() {
    if (window.confirm("Leave this workspace? You will lose access until re-invited.")) {
      setActionError(null);
      leave.mutate();
    }
  }

  const members = membersQuery.data ?? [];
  const invitations = invitationsQuery.data ?? [];
  const busy = changeRole.isPending || setActive.isPending || transfer.isPending;

  return (
    <section aria-labelledby="members-title" className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Administration</p>
          <h1 id="members-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Members
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Everyone with access to this workspace. Invitations are emailed as a secure link — tokens
            are never shown here.
          </p>
        </div>
        <div className="flex gap-2">
          <Button type="button" variant="ghost" onClick={handleLeave} disabled={leave.isPending}>
            Leave workspace
          </Button>
          <Button type="button" variant="secondary" onClick={() => setInviteOpen((open) => !open)}>
            {inviteOpen ? "Cancel invite" : "Invite member"}
          </Button>
        </div>
      </div>

      {actionError ? (
        <p role="alert" className="rounded-lg border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">
          {actionError}
        </p>
      ) : null}

      {inviteOpen ? (
        <form
          className="grid gap-4 rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm sm:grid-cols-[1fr_10rem_auto] sm:items-end"
          onSubmit={handleInvite}
        >
          <Input id="invite-email" name="email" type="email" label="Email" required placeholder="teammate@example.com" />
          <div className="grid gap-2">
            <label htmlFor="invite-role" className="text-sm font-medium">
              Role
            </label>
            <select
              id="invite-role"
              name="role"
              defaultValue="Member"
              className="h-11 rounded-lg border border-border bg-background px-3 text-sm shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              {roles.map((role) => (
                <option key={role} value={role}>
                  {role}
                </option>
              ))}
            </select>
          </div>
          <Button type="submit" disabled={invite.isPending}>
            {invite.isPending ? "Sending…" : "Send invite"}
          </Button>
          {invite.error ? (
            <p role="alert" className="text-sm text-red-600 sm:col-span-3 dark:text-red-400">
              {errorMessage(invite.error, "Invite failed.")}
            </p>
          ) : null}
          {invite.isSuccess ? (
            <p className="text-sm text-muted-foreground sm:col-span-3">Invitation sent. It appears below as pending.</p>
          ) : null}
        </form>
      ) : null}

      {invitations.length > 0 ? (
        <div className="overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-sm">
          <h2 className="border-b border-border px-4 py-3 text-sm font-semibold">Pending invitations</h2>
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-left text-sm">
            <caption className="sr-only">Pending invitations</caption>
            <thead className="bg-muted text-muted-foreground">
              <tr>
                <th scope="col" className="px-4 py-3 font-medium">Email</th>
                <th scope="col" className="px-4 py-3 font-medium">Role</th>
                <th scope="col" className="px-4 py-3 font-medium">Expires</th>
                <th scope="col" className="px-4 py-3 font-medium text-right">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {invitations.map((invitation) => (
                <tr key={invitation.id}>
                  <td className="px-4 py-4 font-medium">{invitation.email}</td>
                  <td className="px-4 py-4">{invitation.role}</td>
                  <td className="px-4 py-4 text-muted-foreground">{formatDate(invitation.expiresAtUtc)}</td>
                  <td className="px-4 py-4">
                    <div className="flex justify-end gap-2">
                      <Button type="button" variant="ghost" onClick={() => { setActionError(null); resend.mutate(invitation.id); }} disabled={resend.isPending}>
                        Resend
                      </Button>
                      <Button type="button" variant="ghost" onClick={() => { setActionError(null); revoke.mutate(invitation.id); }} disabled={revoke.isPending}>
                        Revoke
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
            </table>
          </div>
        </div>
      ) : null}

      {/* `overflow-hidden` alone (which is all the rounded corners need) meant a four-column table on a
          phone was cropped at the viewport edge with no way to reach the Actions column. */}
      <div className="overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-sm">
        <div className="overflow-x-auto">
        <table className="w-full min-w-[36rem] border-collapse text-left text-sm">
          <caption className="sr-only">Workspace members</caption>
          <thead className="bg-muted text-muted-foreground">
            <tr>
              <th scope="col" className="px-4 py-3 font-medium">Name</th>
              <th scope="col" className="px-4 py-3 font-medium">Email</th>
              <th scope="col" className="px-4 py-3 font-medium">Role</th>
              <th scope="col" className="px-4 py-3 font-medium">Status</th>
              <th scope="col" className="px-4 py-3 font-medium text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {membersQuery.isPending ? (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-muted-foreground">
                  Loading members…
                </td>
              </tr>
            ) : membersQuery.error ? (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-red-600 dark:text-red-400">
                  {errorMessage(membersQuery.error, "Could not load members.")}
                </td>
              </tr>
            ) : members.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-muted-foreground">
                  No members yet.
                </td>
              </tr>
            ) : (
              members.map((member) => {
                const isSelf = member.userId === currentUserId;
                const deactivated = member.status !== "Active";
                return (
                  <tr key={member.id}>
                    <td className="px-4 py-4 font-medium">
                      {member.displayName ?? member.email ?? "Member"}
                      {isSelf ? <span className="ml-2 text-xs text-muted-foreground">(you)</span> : null}
                    </td>
                    <td className="px-4 py-4 text-muted-foreground">{member.email ?? "—"}</td>
                    <td className="px-4 py-4">
                      <label className="sr-only" htmlFor={`role-${member.id}`}>Role for {member.displayName ?? member.email ?? member.id}</label>
                      <select
                        id={`role-${member.id}`}
                        value={member.role}
                        disabled={busy}
                        onChange={(event) => { setActionError(null); changeRole.mutate({ membershipId: member.id, role: event.target.value }); }}
                        className="h-9 rounded-lg border border-border bg-background px-2 text-sm shadow-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                      >
                        {roles.map((role) => (
                          <option key={role} value={role}>{role}</option>
                        ))}
                      </select>
                    </td>
                    <td className="px-4 py-4">
                      <span className="rounded-full bg-muted px-2.5 py-1 text-xs font-medium text-muted-foreground">
                        {member.status}
                      </span>
                    </td>
                    <td className="px-4 py-4">
                      <div className="flex justify-end gap-2">
                        {!isSelf && member.role !== "Owner" ? (
                          <Button type="button" variant="ghost" onClick={() => handleTransfer(member)} disabled={busy}>
                            Make owner
                          </Button>
                        ) : null}
                        {!isSelf ? (
                          <Button
                            type="button"
                            variant="ghost"
                            onClick={() => { setActionError(null); setActive.mutate({ membershipId: member.id, activate: deactivated }); }}
                            disabled={busy}
                          >
                            {deactivated ? "Reactivate" : "Deactivate"}
                          </Button>
                        ) : null}
                      </div>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
        </div>
      </div>

      <TeamsPanel />
    </section>
  );
}
