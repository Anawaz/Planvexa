import { apiClient, proxyHref } from "@/lib/api-client";
import type {
  AuditEntry,
  AuditSearchInput,
  EnterpriseSecuritySettings,
  ExportJob,
  UpdateSecuritySettingsInput,
} from "./types";

// ---- governance ----

function auditQuery(input: AuditSearchInput): Record<string, string | undefined> {
  return {
    action: input.action,
    entityType: input.entityType,
    actorUserId: input.actorUserId,
    from: input.from,
    to: input.to,
  };
}

export function searchAudit(input: AuditSearchInput = {}) {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(auditQuery(input))) {
    if (value) query.set(key, value);
  }
  const suffix = query.toString();
  return apiClient.get<AuditEntry[]>(`/governance/audit${suffix ? `?${suffix}` : ""}`);
}

/** Browser download link — goes through the BFF proxy so the session cookie authenticates it. */
export function auditExportHref(input: AuditSearchInput = {}) {
  return proxyHref("/governance/audit/export", auditQuery(input));
}

export function getSecuritySettings() {
  return apiClient.get<EnterpriseSecuritySettings>("/governance/security-settings");
}

export function updateSecuritySettings(input: UpdateSecuritySettingsInput) {
  return apiClient.put<EnterpriseSecuritySettings>("/governance/security-settings", {
    ssoEnabled: input.ssoEnabled,
    samlEntityId: input.samlEntityId ?? null,
    samlMetadataUrl: input.samlMetadataUrl ?? null,
    scimEnabled: input.scimEnabled,
    scimToken: input.scimToken?.trim() ? input.scimToken.trim() : null,
    mfaRequired: input.mfaRequired,
  });
}

export function listExports() {
  return apiClient.get<ExportJob[]>("/governance/exports");
}

export function createExport(dataset: string) {
  return apiClient.post<ExportJob>("/governance/exports", { dataset });
}

export function getExport(id: string) {
  return apiClient.get<ExportJob>(`/governance/exports/${id}`);
}

export function exportDownloadHref(id: string) {
  return proxyHref(`/governance/exports/${id}/download`);
}
