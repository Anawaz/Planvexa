import type { HostActivityInput, HostListInput, InstanceLogInput } from "./types";

/**
 * Query keys for the host console.
 *
 * Unlike `adminKeys` in `@/lib/admin/queries`, none of these are workspace-scoped — this data
 * describes the whole installation, so keying by workspace would be wrong and would also fragment the
 * cache every time the user's ambient workspace changed underneath them.
 */
export const hostKeys = {
  all: ["host"] as const,
  status: () => [...hostKeys.all, "status"] as const,
  overview: () => [...hostKeys.all, "overview"] as const,
  workspacesRoot: () => [...hostKeys.all, "workspaces"] as const,
  workspaces: (input: HostListInput = {}) => [...hostKeys.workspacesRoot(), "list", input] as const,
  workspace: (workspaceId: string) => [...hostKeys.workspacesRoot(), workspaceId] as const,
  workspaceUsage: (workspaceId: string) => [...hostKeys.workspace(workspaceId), "usage"] as const,
  usersRoot: () => [...hostKeys.all, "users"] as const,
  users: (input: HostListInput = {}) => [...hostKeys.usersRoot(), "list", input] as const,
  user: (userId: string) => [...hostKeys.usersRoot(), userId] as const,
  activity: (input: HostActivityInput = {}) => [...hostKeys.all, "activity", input] as const,
  logs: (input: InstanceLogInput = {}) => [...hostKeys.all, "logs", input] as const,
  health: () => [...hostKeys.all, "health"] as const,
  settings: () => [...hostKeys.all, "settings"] as const,
};
