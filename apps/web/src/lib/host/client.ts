import { apiClient } from "@/lib/api-client";
import type {
  HostActivityEntry,
  HostActivityInput,
  HostListInput,
  HostOverview,
  HostPage,
  HostUserDetail,
  HostUserSummary,
  HostWorkspaceDetail,
  HostWorkspaceSummary,
  HostWorkspaceUsage,
  InstanceHealth,
  InstanceLogEntry,
  InstanceLogInput,
  InstanceSettings,
  UpdateInstanceSettingsInput,
} from "./types";

/**
 * Every call here passes `noWorkspace` — `/api/v1/host/*` is instance-level, and an `X-Workspace`
 * header would make the API resolve a workspace whose RLS policies then filter every cross-workspace
 * row down to that one. The ambient workspace in `apiClient` is module state that survives a
 * client-side navigation out of `/app`, so opting out has to be explicit rather than assumed.
 */
const host = { noWorkspace: true } as const;

function query(input: Record<string, string | number | undefined>) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(input)) {
    if (value !== undefined && value !== "") params.set(key, String(value));
  }

  const suffix = params.toString();
  return suffix ? `?${suffix}` : "";
}

/**
 * "Am I a host administrator?" — authenticated but not host-gated, so the ordinary shell can ask
 * without provoking a 403 it would have to swallow. This decides what to render; the API's policy is
 * what actually enforces access.
 */
export function getHostAdminStatus() {
  return apiClient.get<{ isHostAdmin: boolean }>("/users/me/host-admin", host);
}

export function getHostOverview() {
  return apiClient.get<HostOverview>("/host/overview", host);
}

// ---- workspaces ----

export function listHostWorkspaces(input: HostListInput = {}) {
  return apiClient.get<HostPage<HostWorkspaceSummary>>(`/host/workspaces${query(input)}`, host);
}

export function getHostWorkspace(workspaceId: string) {
  return apiClient.get<HostWorkspaceDetail>(`/host/workspaces/${workspaceId}`, host);
}

/** Separate from the detail call because it costs a round trip into the target workspace. */
export function getHostWorkspaceUsage(workspaceId: string) {
  return apiClient.get<HostWorkspaceUsage>(`/host/workspaces/${workspaceId}/usage`, host);
}

export function suspendHostWorkspace(workspaceId: string) {
  return apiClient.post<{ status: string }>(`/host/workspaces/${workspaceId}/suspend`, undefined, host);
}

export function restoreHostWorkspace(workspaceId: string) {
  return apiClient.post<{ status: string }>(`/host/workspaces/${workspaceId}/restore`, undefined, host);
}

/** Irreversible. `confirmSlug` must match the workspace's slug exactly or the API returns 409. */
export function deleteHostWorkspace(workspaceId: string, confirmSlug: string) {
  return apiClient.post<void>(`/host/workspaces/${workspaceId}/delete`, { confirmSlug }, host);
}

// ---- users ----

export function listHostUsers(input: HostListInput = {}) {
  return apiClient.get<HostPage<HostUserSummary>>(`/host/users${query(input)}`, host);
}

export function getHostUser(userId: string) {
  return apiClient.get<HostUserDetail>(`/host/users/${userId}`, host);
}

export function setHostUserActive(userId: string, active: boolean) {
  return apiClient.post<void>(`/host/users/${userId}/${active ? "enable" : "disable"}`, undefined, host);
}

export function setHostUserHostAdmin(userId: string, granted: boolean) {
  return apiClient.post<void>(`/host/users/${userId}/host-admin`, { granted }, host);
}

// ---- activity, logs, health, settings ----

export function listHostActivity(input: HostActivityInput = {}) {
  return apiClient.get<HostPage<HostActivityEntry>>(`/host/activity${query(input)}`, host);
}

/**
 * Browser download link — a plain `<a href>` through the BFF proxy, so the session cookie
 * authenticates it. Deliberately not `proxyHref`: that helper appends the ambient workspace as an
 * `x-workspace` query param, which the proxy turns back into a header — exactly what a host request
 * must not carry.
 */
export function hostActivityExportHref(input: HostActivityInput = {}) {
  // Paging is dropped: the export covers everything matching the current filters (server-capped),
  // not just the page on screen.
  return `/api/proxy/host/activity/export${query({ ...input, skip: undefined, take: undefined })}`;
}

export function listInstanceLogs(input: InstanceLogInput = {}) {
  return apiClient.get<HostPage<InstanceLogEntry>>(`/host/logs${query(input)}`, host);
}

export function getInstanceHealth() {
  return apiClient.get<InstanceHealth>("/host/health", host);
}

export function getInstanceSettings() {
  return apiClient.get<InstanceSettings>("/host/settings", host);
}

export function updateInstanceSettings(input: UpdateInstanceSettingsInput) {
  return apiClient.put<InstanceSettings>("/host/settings", input, host);
}
