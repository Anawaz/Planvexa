"use client";

import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient, isMfaRequiredError, setApiContext } from "@/lib/api-client";
import { clearCacheForWorkspace } from "@/lib/offline/db";
import { setFormatPreferences } from "@/lib/i18n/formatPreferences";

type SessionUser = { subject: string; email?: string; name?: string };
type CurrentUser = {
  userId: string;
  email: string;
  displayName: string;
  avatarUrl?: string | null;
  timezone?: string | null;
  locale?: string | null;
  theme?: string | null;
};
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
  /** True when the current Workspace requires MFA and this session hasn't completed a second factor
   * (WorkspaceResolutionMiddleware and WorkspaceHub both enforce this server-side) — the app shell
   * renders a dedicated remediation screen instead of the normal layout while this is true. */
  mfaRequired: boolean;
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
  setFormatPreferences({ locale: currentUser?.locale ?? undefined, timeZone: currentUser?.timezone ?? undefined });

  const featuresQuery = useQuery({
    queryKey: ["features", workspaceId],
    queryFn: () => apiClient.get<Feature[]>("/features", { workspaceId }),
    enabled: Boolean(workspaceId),
  });
  const mfaRequired = isMfaRequiredError(featuresQuery.error);

  const value = useMemo<AppContextValue>(() => ({
    user: sessionQuery.data?.user ?? null,
    currentUser,
    currentUserId,
    workspaces,
    currentWorkspace,
    features: featuresQuery.data ?? [],
    workspaceId,
    // membershipsQuery is gated by `enabled: sessionQuery.data?.user != null`, so right after the
    // session resolves there is one render where it has not started fetching yet — its isLoading
    // (isPending && isFetching) reads false in that window even though no membership data has
    // arrived. isPending alone (ignores fetchStatus) closes that gap without hanging forever for a
    // logged-out user, since it's only checked once we know a user is signed in.
    isLoading: sessionQuery.isLoading || (sessionQuery.data?.user != null && membershipsQuery.isPending),
    mfaRequired,
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
  }), [sessionQuery.data?.user, sessionQuery.isLoading, currentUser, currentUserId, workspaces, currentWorkspace, featuresQuery.data, workspaceId, membershipsQuery.isPending, queryClient, mfaRequired]);

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}

export function useAppContext() {
  const context = useContext(AppContext);
  if (!context) throw new Error("useAppContext must be used within AppContextProvider");
  return context;
}
