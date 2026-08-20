// Shapes mirror src/Infrastructure/Planvexa.Infrastructure/HostAdmin/HostAdminContracts.cs and the
// health/settings/log responses in apps/api Endpoints/HostEndpoints.cs. Status fields are plain
// strings: the API serializes domain enums by name.
//
// METADATA ONLY, deliberately. Nothing here carries task titles, document bodies or messages —
// Workspace remains the isolation boundary for content, and host administration is about the
// installation rather than the work inside it. If a field like that ever appears in this file,
// something has gone wrong on the server side.

export type HostPage<T> = {
  items: T[];
  total: number;
};

export type HostWorkspaceSummary = {
  id: string;
  name: string;
  slug: string;
  status: string;
  createdAtUtc: string;
  ownerUserId?: string | null;
  ownerDisplayName?: string | null;
  ownerEmail?: string | null;
  memberCount: number;
  lastActivityAtUtc?: string | null;
};

export type HostWorkspaceMember = {
  membershipId: string;
  userId: string;
  displayName?: string | null;
  email?: string | null;
  role: string;
  status: string;
  isGuest: boolean;
  joinedAtUtc: string;
};

export type HostWorkspaceDetail = {
  summary: HostWorkspaceSummary;
  enabledFeatures: string[];
  members: HostWorkspaceMember[];
};

export type HostWorkspaceUsage = {
  workspaceId: string;
  spaces: number;
  lists: number;
  tasks: number;
  documents: number;
  attachments: number;
  attachmentBytes: number;
};

export type HostUserSummary = {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  isHostAdmin: boolean;
  isAnonymized: boolean;
  createdAtUtc: string;
  lastSeenAtUtc?: string | null;
  workspaceCount: number;
};

export type HostUserMembership = {
  workspaceId: string;
  workspaceName: string;
  workspaceSlug: string;
  workspaceStatus: string;
  role: string;
  status: string;
  joinedAtUtc: string;
};

export type HostUserDetail = {
  summary: HostUserSummary;
  memberships: HostUserMembership[];
};

export type HostMonthlyCount = { year: number; month: number; count: number };

export type HostActivityEntry = {
  id: string;
  createdAtUtc: string;
  action: string;
  entityType: string;
  entityId?: string | null;
  actorUserId?: string | null;
  actorDisplayName?: string | null;
  workspaceId?: string | null;
  workspaceName?: string | null;
  ipAddress?: string | null;
};

export type HostOverview = {
  activeWorkspaces: number;
  archivedWorkspaces: number;
  activeUsers: number;
  disabledUsers: number;
  hostAdmins: number;
  memberships: number;
  usersSeenLast7Days: number;
  usersSeenLast30Days: number;
  workspacesCreatedByMonth: HostMonthlyCount[];
  recentActivity: HostActivityEntry[];
};

export type InstanceHealth = {
  databaseReachable: boolean;
  databaseVersion?: string | null;
  appliedScripts: number;
  latestScript?: string | null;
  outboxPending: number;
  outboxFailed: number;
  errorsLast24Hours: number;
  warningsLast24Hours: number;
  droppedLogRecords: number;
  logCaptureEnabled: boolean;
  logMinimumLevel: string;
  logRetentionDays: number;
  fileStorageProvider: string;
  emailSender: string;
  maintenanceConnectionConfigured: boolean;
  version?: string | null;
  environment: string;
};

export type InstanceLogEntry = {
  id: string;
  createdAtUtc: string;
  level: string;
  category: string;
  message: string;
  exception?: string | null;
  correlationId?: string | null;
  userId?: string | null;
  workspaceId?: string | null;
};

export type WorkspaceCreationPolicy = "Anyone" | "HostAdminsOnly";

/**
 * Whether the identity provider will let anyone create an account at all — the OTHER half of
 * self-registration. Planvexa's own toggle only decides whether it accepts a new identity; if the IdP
 * refuses to create one, the sign-up link fails no matter what this instance says.
 */
export type IdentityProviderState = {
  /** True when Planvexa holds credentials to read and change the IdP setting. */
  manageable: boolean;
  /** The IdP's state, or null when it could not be determined. */
  registrationAllowed: boolean | null;
  detail?: string | null;
};

export type InstanceSettings = {
  allowSelfRegistration: boolean;
  workspaceCreationPolicy: WorkspaceCreationPolicy;
  instanceName?: string | null;
  logoUrl?: string | null;
  supportEmail?: string | null;
  updatedAtUtc?: string | null;
  updatedByUserId?: string | null;
  identityProvider: IdentityProviderState;
};

/** Every field optional — the API treats null as "leave this one alone". */
export type UpdateInstanceSettingsInput = {
  allowSelfRegistration?: boolean;
  workspaceCreationPolicy?: WorkspaceCreationPolicy;
  instanceName?: string;
  logoUrl?: string;
  supportEmail?: string;
};

export type HostListInput = {
  search?: string;
  status?: string;
  skip?: number;
  take?: number;
};

export type HostActivityInput = {
  action?: string;
  entityType?: string;
  actorUserId?: string;
  workspaceId?: string;
  from?: string;
  to?: string;
  skip?: number;
  take?: number;
};

export type InstanceLogInput = {
  level?: string;
  category?: string;
  search?: string;
  from?: string;
  to?: string;
  skip?: number;
  take?: number;
};
