// Shapes mirror Governance/Application/Contracts.cs.
// Status fields are plain strings: the API serializes domain enums by name.

export type ExportDataset = "audit" | "tasks";

export type EnterpriseSecuritySettings = {
  ssoEnabled: boolean;
  samlEntityId?: string | null;
  samlMetadataUrl?: string | null;
  scimEnabled: boolean;
  scimTokenSet: boolean;
  mfaRequired: boolean;
};

export type AuditEntry = {
  id: string;
  actorUserId?: string | null;
  action: string;
  entityType: string;
  entityId?: string | null;
  ipAddress?: string | null;
  createdAtUtc: string;
};

export type ExportJob = {
  id: string;
  dataset: string;
  status: string;
  createdAtUtc: string;
  completedAtUtc?: string | null;
  rowCount?: number | null;
};

export type AuditSearchInput = {
  action?: string;
  entityType?: string;
  actorUserId?: string;
  from?: string;
  to?: string;
};

export type UpdateSecuritySettingsInput = EnterpriseSecuritySettings & {
  scimToken?: string;
};
