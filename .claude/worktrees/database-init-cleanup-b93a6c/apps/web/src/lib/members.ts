"use client";

import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/api-client";
import { useAppContext } from "@/lib/app-context/AppContext";

export type Member = {
  id: string;
  userId: string;
  role: string;
  status: string;
  isGuest: boolean;
  joinedAtUtc: string;
  displayName?: string | null;
  email?: string | null;
};

export function useMembers() {
  const { workspaceId } = useAppContext();

  return useQuery({
    queryKey: ["members", workspaceId],
    queryFn: () => apiClient.get<Member[]>(`/workspaces/${workspaceId}/members`),
    enabled: Boolean(workspaceId),
  });
}

export type PendingInvitation = {
  id: string;
  email: string;
  role: string;
  status: string;
  createdAtUtc: string;
  expiresAtUtc: string;
};

export function usePendingInvitations() {
  const { workspaceId } = useAppContext();

  return useQuery({
    queryKey: ["invitations", workspaceId],
    queryFn: () => apiClient.get<PendingInvitation[]>(`/workspaces/${workspaceId}/invitations`),
    enabled: Boolean(workspaceId),
  });
}

export type Team = {
  id: string;
  workspaceId: string;
  name: string;
  description?: string | null;
  isArchived: boolean;
  memberCount: number;
};

export type TeamMember = { userId: string; addedAtUtc: string };

export function useTeams() {
  const { workspaceId } = useAppContext();

  return useQuery({
    queryKey: ["teams", workspaceId],
    queryFn: () => apiClient.get<Team[]>(`/workspaces/${workspaceId}/teams`),
    enabled: Boolean(workspaceId),
  });
}

export function useTeamMembers(teamId: string | null) {
  return useQuery({
    queryKey: ["team-members", teamId],
    queryFn: () => apiClient.get<TeamMember[]>(`/teams/${teamId}/members`),
    enabled: Boolean(teamId),
  });
}

function initialsOf(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0])
    .join("")
    .toUpperCase();
}

/**
 * The signed-in user's internal user id, resolved server-side from the bearer token via
 * `GET /api/v1/users/me` (see AppContext). The UI needs it to decide which comments/messages offer
 * Edit/Delete. No email matching (ADR 0015).
 */
export function useCurrentUserId() {
  return useAppContext().currentUserId;
}

/** Label/initials for a user id; unknown ids (former members, system actors) fall back to the id. */
export function useMemberDirectory() {
  const { data } = useMembers();

  return useMemo(() => {
    const byUserId = new Map((data ?? []).map((member) => [member.userId, member]));
    const nameOf = (userId: string) => {
      const member = byUserId.get(userId);
      return member?.displayName || member?.email || null;
    };

    return {
      getLabel: (userId: string) => nameOf(userId) ?? userId,
      getInitials: (userId: string) => {
        const name = nameOf(userId);
        return name ? initialsOf(name) : userId.slice(0, 2).toUpperCase();
      },
    };
  }, [data]);
}
