import type { AuditSearchInput } from "./types";

export const adminKeys = {
  all: ["admin"] as const,
  security: (workspaceId: string) => [...adminKeys.all, "security", workspaceId] as const,
  auditRoot: (workspaceId: string) => [...adminKeys.all, "audit", workspaceId] as const,
  audit: (workspaceId: string, input: AuditSearchInput = {}) =>
    [...adminKeys.auditRoot(workspaceId), "search", input] as const,
  exportsRoot: (workspaceId: string) => [...adminKeys.all, "exports", workspaceId] as const,
  exports: (workspaceId: string) => [...adminKeys.exportsRoot(workspaceId), "list"] as const,
  export: (workspaceId: string, id: string) => [...adminKeys.exportsRoot(workspaceId), id] as const,
};
