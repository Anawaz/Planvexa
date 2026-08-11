import type { ListNotificationsInput } from "./types";

// Workspace-scoped resources carry the workspace id so AppContext's invalidate-on-switch cannot
// leave another workspace's rows on screen. Comments/shares key off a task id (globally unique);
// notifications and preferences are workspace-scoped, per-user.
export const collabKeys = {
  all: ["collab"] as const,
  commentsRoot: () => [...collabKeys.all, "comments"] as const,
  comments: (taskId: string) => [...collabKeys.commentsRoot(), taskId] as const,
  notificationsRoot: () => [...collabKeys.all, "notifications"] as const,
  notifications: (input: ListNotificationsInput = {}) =>
    [...collabKeys.notificationsRoot(), "list", input] as const,
  unreadCount: () => [...collabKeys.all, "unread-count"] as const,
  preferences: () => [...collabKeys.all, "preferences"] as const,
  digestPreference: () => [...collabKeys.all, "digest-preference"] as const,
  shares: (taskId: string) => [...collabKeys.all, "shares", taskId] as const,
  shareComments: (shareId: string) => [...collabKeys.all, "share-comments", shareId] as const,
  shareAccessLog: (shareId: string) => [...collabKeys.all, "share-access-log", shareId] as const,
  presence: (workspaceId: string) => [...collabKeys.all, "presence", workspaceId] as const,
  documentsRoot: (workspaceId: string) => [...collabKeys.all, "documents", workspaceId] as const,
  documents: (workspaceId: string) => [...collabKeys.documentsRoot(workspaceId), "list"] as const,
  document: (workspaceId: string, id: string) => [...collabKeys.documentsRoot(workspaceId), id] as const,
  documentVersions: (workspaceId: string, id: string) =>
    [...collabKeys.document(workspaceId, id), "versions"] as const,
  documentComments: (workspaceId: string, id: string) =>
    [...collabKeys.document(workspaceId, id), "comments"] as const,
  documentShares: (workspaceId: string, id: string) =>
    [...collabKeys.document(workspaceId, id), "shares"] as const,
  documentPermissions: (workspaceId: string, id: string) =>
    [...collabKeys.document(workspaceId, id), "permissions"] as const,
  formsRoot: (workspaceId: string) => [...collabKeys.all, "forms", workspaceId] as const,
  forms: (workspaceId: string) => [...collabKeys.formsRoot(workspaceId), "list"] as const,
  form: (workspaceId: string, id: string) => [...collabKeys.formsRoot(workspaceId), id] as const,
  formSubmissions: (workspaceId: string, id: string) =>
    [...collabKeys.form(workspaceId, id), "submissions"] as const,
  automationsRoot: (workspaceId: string) => [...collabKeys.all, "automations", workspaceId] as const,
  automations: (workspaceId: string) => [...collabKeys.automationsRoot(workspaceId), "list"] as const,
  automation: (workspaceId: string, id: string) =>
    [...collabKeys.automationsRoot(workspaceId), id] as const,
  automationRuns: (workspaceId: string, id: string) =>
    [...collabKeys.automation(workspaceId, id), "runs"] as const,
  webhooksRoot: (workspaceId: string) => [...collabKeys.all, "webhooks", workspaceId] as const,
  webhooks: (workspaceId: string) => [...collabKeys.webhooksRoot(workspaceId), "list"] as const,
  webhookDeliveries: (workspaceId: string, id: string) =>
    [...collabKeys.webhooksRoot(workspaceId), id, "deliveries"] as const,
  tokens: (workspaceId: string) => [...collabKeys.all, "personal-access-tokens", workspaceId] as const,
  oauthApplications: (workspaceId: string) => [...collabKeys.all, "oauth-applications", workspaceId] as const,
  providerSettings: (workspaceId: string) => [...collabKeys.all, "provider-settings", workspaceId] as const,
  importSources: () => [...collabKeys.all, "import-sources"] as const,
  importJobsRoot: (workspaceId: string) => [...collabKeys.all, "import-jobs", workspaceId] as const,
  importJobs: (workspaceId: string) => [...collabKeys.importJobsRoot(workspaceId), "list"] as const,
  importJob: (workspaceId: string, id: string) => [...collabKeys.importJobsRoot(workspaceId), id] as const,
  importJobRows: (workspaceId: string, id: string) =>
    [...collabKeys.importJob(workspaceId, id), "rows"] as const,
  whiteboardsRoot: (workspaceId: string) => [...collabKeys.all, "whiteboards", workspaceId] as const,
  whiteboards: (workspaceId: string) => [...collabKeys.whiteboardsRoot(workspaceId), "list"] as const,
  whiteboard: (workspaceId: string, id: string) => [...collabKeys.whiteboardsRoot(workspaceId), id] as const,
  whiteboardTemplates: (workspaceId: string) => [...collabKeys.whiteboardsRoot(workspaceId), "templates"] as const,
  clipsRoot: (workspaceId: string) => [...collabKeys.all, "clips", workspaceId] as const,
  clips: (workspaceId: string) => [...collabKeys.clipsRoot(workspaceId), "list"] as const,
  clip: (workspaceId: string, id: string) => [...collabKeys.clipsRoot(workspaceId), id] as const,
  clipComments: (workspaceId: string, id: string) => [...collabKeys.clip(workspaceId, id), "comments"] as const,
  clipTranscript: (workspaceId: string, id: string) => [...collabKeys.clip(workspaceId, id), "transcript"] as const,
};
