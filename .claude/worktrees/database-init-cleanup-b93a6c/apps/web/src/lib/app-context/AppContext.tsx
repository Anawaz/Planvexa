"use client";

import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient, setApiContext } from "@/lib/api-client";
import { clearCacheForWorkspace } from "@/lib/offline/db";

type SessionUser = { subject: string; email?: string; name?: string };
type CurrentUser = { userId: string; email: string; displayName: string };
type Workspace = { id: string; name: string; slug: string; status: string; role: string };
// Wire shape of GET /workspaces/me (WorkspaceDto) — every workspace the user belongs to, with their role.
type WorkspaceMembership = {
  id: string;
  name: string;
  slug: string;
  status: string;
  createdAtUtc: string;
  role: string;
};
type Feature = { key: string; enabled: boolean; limit?: number | null; source: string };

type AppContextValue = {
  user: SessionUser | null;
  currentUser: CurrentUser | null;
  currentUserId?: string;
  workspaces: Workspace[];
  currentWorkspace: Workspace | null;
  features: Feature[];
  setCurrentWorkspaceId: (workspaceId: string) => void;
  workspaceId?: string;
  isLoading: boolean;
};

const AppContext = createContext<AppContextValue | null>(null);

function readStorage(key: string) {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem(key);
}

function writeStorage(key: string, value: string) {
  if (typeof window !== "undefined") window.localStorage.setItem(key, value);
}

export function AppContextProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [selectedWorkspaceId, setSelectedWorkspaceIdState] = useState<string | null>(() => readStorage("planvexa-active-workspace"));

  const sessionQuery = useQuery({
    queryKey: ["session", "me"],
    queryFn: async () => (await fetch("/api/session/me", { cache: "no-store" })).json() as Promise<{ user: SessionUser | null }>,
  });

  // Global identity (ADR 0015): the authenticated user's internal id, resolved by the API from the
  // bearer token — never by matching an email. Query key stays unscoped by workspace.
  const currentUserQuery = useQuery({
    queryKey: ["user", "me"],
    queryFn: () => apiClient.get<CurrentUser>("/users/me"),
    enabled: sessionQuery.data?.user != null,
  });
  const currentUser = currentUserQuery.data ?? null;
  const currentUserId = currentUser?.userId;

  // Workspace is the single top-level product concept (ADR 0015): one flat list of every workspace
  // the user belongs to.
  const membershipsQuery = useQuery({
    queryKey: ["workspaces", "me"],
    queryFn: () => apiClient.get<WorkspaceMembership[]>("/workspaces/me"),
    enabled: sessionQuery.data?.user != null,
  });
  const workspaces = useMemo<Workspace[]>(
    () => (membershipsQuery.data ?? []).map((m) => ({
      id: m.id,
      name: m.name,
      slug: m.slug,
      status: m.status,
      role: m.role,
    })),
    [membershipsQuery.data],
  );

  // Resolve the active workspace: explicit selection → last stored workspace for this user → shared
  // pinned workspace → first available.
  const storedWorkspaceId = currentUserId ? readStorage(`planvexa-active-workspace:${currentUserId}`) : null;
  const currentWorkspace =
    workspaces.find((workspace) => workspace.id === selectedWorkspaceId)
    ?? (storedWorkspaceId ? workspaces.find((workspace) => workspace.id === storedWorkspaceId) : undefined)
    ?? workspaces[0]
    ?? null;
  const workspaceId = currentWorkspace?.id;

  useEffect(() => {
    if (currentWorkspace) {
      writeStorage("planvexa-active-workspace", currentWorkspace.id);
      if (currentUserId) {
        writeStorage(`planvexa-active-workspace:${currentUserId}`, currentWorkspace.id);
      }
    }
  }, [currentWorkspace, currentUserId]);

  // Synchronously during render: an effect would run after children's queries have already fired.
  setApiContext({ workspaceId });

  const featuresQuery = useQuery({
    queryKey: ["features", workspaceId],
    queryFn: () => apiClient.get<Feature[]>("/features", { workspaceId }),
    enabled: Boolean(workspaceId),
  });

  const value = useMemo<AppContextValue>(() => ({
    user: sessionQuery.data?.user ?? null,
    currentUser,
    currentUserId,
    workspaces,
    currentWorkspace,
    features: featuresQuery.data ?? [],
    workspaceId,
    isLoading: sessionQuery.isLoading || membershipsQuery.isLoading,
    setCurrentWorkspaceId: (id: string) => {
      const previousWorkspaceId = workspaceId;
      setSelectedWorkspaceIdState(id);
      setApiContext({ workspaceId: id });
      // Cancel requests still in flight for the previous workspace before refetching, so their
      // late responses cannot land in the new workspace's cache.
      void queryClient.cancelQueries();
      void queryClient.invalidateQueries();
      // The IndexedDB offline read-cache (tasks/comments/time-entries) is scoped by
      // workspace exactly like the in-memory query cache above — clear the OUTGOING workspace's
      // entries so they can never render after switching in. The outbox (queued offline edits) is
      // deliberately NOT cleared here: it must keep syncing regardless of which workspace is active.
      if (previousWorkspaceId && previousWorkspaceId !== id) {
        void clearCacheForWorkspace(previousWorkspaceId);
      }
    },
  }), [sessionQuery.data?.user, sessionQuery.isLoading, currentUser, currentUserId, workspaces, currentWorkspace, featuresQuery.data, workspaceId, membershipsQuery.isLoading, queryClient]);

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}

export function useAppContext() {
  const context = useContext(AppContext);
  if (!context) throw new Error("useAppContext must be used within AppContextProvider");
  return context;
}
