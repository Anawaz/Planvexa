"use client";

import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { MemberSelect } from "@/components/people/MemberSelect";
import { ApiError, apiClient } from "@/lib/api-client";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useMemberDirectory, useTeamMembers, useTeams } from "@/lib/members";

export function TeamsPanel() {
  const { workspaceId } = useAppContext();
  const queryClient = useQueryClient();
  const teamsQuery = useTeams();
  const directory = useMemberDirectory();
  const [creating, setCreating] = useState(false);
  const [selectedTeamId, setSelectedTeamId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const teams = teamsQuery.data ?? [];
  const membersQuery = useTeamMembers(selectedTeamId);

  function invalidateTeams() {
    queryClient.invalidateQueries({ queryKey: ["teams", workspaceId] });
  }
  function invalidateTeamMembers() {
    queryClient.invalidateQueries({ queryKey: ["team-members", selectedTeamId] });
  }
  function fail(err: unknown, fallback: string) {
    setError(err instanceof ApiError ? err.message : fallback);
  }

  const createTeam = useMutation({
    mutationFn: (body: { name: string; description?: string }) =>
      apiClient.post(`/workspaces/${workspaceId}/teams`, body),
    onSuccess: () => {
      invalidateTeams();
      setCreating(false);
    },
    onError: (err) => fail(err, "Could not create team."),
  });

  const deleteTeam = useMutation({
    mutationFn: (teamId: string) => apiClient.delete(`/teams/${teamId}`),
    onSuccess: () => {
      invalidateTeams();
      setSelectedTeamId(null);
    },
    onError: (err) => fail(err, "Could not delete team."),
  });

  const addMember = useMutation({
    mutationFn: (userId: string) => apiClient.post(`/teams/${selectedTeamId}/members`, { userId }),
    onSuccess: () => {
      invalidateTeamMembers();
      invalidateTeams();
    },
    onError: (err) => fail(err, "Could not add member."),
  });

  const removeMember = useMutation({
    mutationFn: (userId: string) => apiClient.delete(`/teams/${selectedTeamId}/members/${userId}`),
    onSuccess: () => {
      invalidateTeamMembers();
      invalidateTeams();
    },
    onError: (err) => fail(err, "Could not remove member."),
  });

  function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    const form = new FormData(event.currentTarget);
    const name = String(form.get("name"));
    const description = String(form.get("description") ?? "");
    createTeam.mutate({ name, description: description || undefined });
  }

  return (
    <section aria-labelledby="teams-title" className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 id="teams-title" className="text-xl font-semibold tracking-tight">
          Teams
        </h2>
        <Button type="button" variant="secondary" onClick={() => setCreating((open) => !open)}>
          {creating ? "Cancel" : "New team"}
        </Button>
      </div>

      {error ? (
        <p role="alert" className="rounded-lg border border-red-300 bg-red-50 px-4 py-2 text-sm text-red-700 dark:border-red-900 dark:bg-red-950/40 dark:text-red-300">
          {error}
        </p>
      ) : null}

      {creating ? (
        <form onSubmit={handleCreate} className="grid gap-3 rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm sm:grid-cols-[1fr_1fr_auto] sm:items-end">
          <Input id="team-name" name="name" label="Name" required placeholder="Engineering" />
          <Input id="team-description" name="description" label="Description" placeholder="Optional" />
          <Button type="submit" disabled={createTeam.isPending}>
            {createTeam.isPending ? "Creating…" : "Create"}
          </Button>
        </form>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,20rem)_1fr]">
        <div className="overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-sm">
          {teamsQuery.isPending ? (
            <p className="px-4 py-6 text-center text-sm text-muted-foreground">Loading teams…</p>
          ) : teams.length === 0 ? (
            <p className="px-4 py-6 text-center text-sm text-muted-foreground">No teams yet.</p>
          ) : (
            <ul className="divide-y divide-border">
              {teams.map((team) => (
                <li key={team.id}>
                  <button
                    type="button"
                    onClick={() => setSelectedTeamId(team.id)}
                    className={`flex w-full items-center justify-between gap-2 px-4 py-3 text-left text-sm hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ${selectedTeamId === team.id ? "bg-muted" : ""}`}
                  >
                    <span className="min-w-0">
                      <span className="block truncate font-medium">{team.name}</span>
                      {team.description ? (
                        <span className="block truncate text-xs text-muted-foreground">{team.description}</span>
                      ) : null}
                    </span>
                    <span className="shrink-0 rounded-full bg-background px-2 py-0.5 text-xs text-muted-foreground">
                      {team.memberCount}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
          {selectedTeamId ? (
            <div className="space-y-4">
              <div className="flex items-center justify-between gap-2">
                <h3 className="text-sm font-semibold">Team members</h3>
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => {
                    if (window.confirm("Delete this team? Members are not affected.")) {
                      deleteTeam.mutate(selectedTeamId);
                    }
                  }}
                  disabled={deleteTeam.isPending}
                >
                  Delete team
                </Button>
              </div>

              <div className="flex items-end gap-2">
                <MemberSelect
                  value=""
                  onChange={(userId) => userId && addMember.mutate(userId)}
                  includeAny
                  anyLabel="Add a member…"
                  className="flex-1"
                  aria-label="Add a member to the team"
                />
              </div>

              {membersQuery.isPending ? (
                <p className="text-sm text-muted-foreground">Loading members…</p>
              ) : (membersQuery.data ?? []).length === 0 ? (
                <p className="text-sm text-muted-foreground">No members on this team yet.</p>
              ) : (
                <ul className="space-y-1">
                  {(membersQuery.data ?? []).map((member) => (
                    <li key={member.userId} className="flex items-center justify-between gap-3 rounded-lg border border-border px-3 py-2 text-sm">
                      <span className="truncate">{directory.getLabel(member.userId)}</span>
                      <button
                        type="button"
                        aria-label="Remove from team"
                        disabled={removeMember.isPending}
                        onClick={() => removeMember.mutate(member.userId)}
                        className="shrink-0 rounded px-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                      >
                        ×
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">Select a team to manage its members.</p>
          )}
        </div>
      </div>
    </section>
  );
}
