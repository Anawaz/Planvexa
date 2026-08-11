import { apiClient, ApiError, proxyHref, type ApiRequestOptions } from "@/lib/api-client";
import type {
  AddCommentInput,
  AutomationRule,
  AutomationRun,
  Clip,
  ClipComment,
  ClipTranscript,
  Comment,
  CreateAutomationInput,
  CreatedToken,
  CreatedWebhook,
  CreatedOAuthApplication,
  CreateDocumentInput,
  CreateFormInput,
  CreateOAuthApplicationInput,
  CreateTokenInput,
  CreateWebhookInput,
  DigestFrequency,
  DigestPreference,
  Document,
  DocumentSummary,
  DocumentTemplate,
  DocumentVersion,
  Form,
  LinkedResourceType,
  Whiteboard,
  WhiteboardTemplate,
  FormSubmission,
  FormSubmitResult,
  FormUploadResult,
  ImportJob,
  ImportJobRow,
  IntegrationProviderSettings,
  ListNotificationsInput,
  Notification,
  NotificationPreference,
  OAuthApplication,
  PersonalAccessToken,
  PreferencePatch,
  PublicComment,
  PublicForm,
  ShareAccessLogEntry,
  ShareLink,
  SharedTask,
  SharePermissionLevel,
  UpdateAutomationInput,
  UpdateDocumentInput,
  UpdateFormInput,
  UpdateFormSettingsInput,
  UpdateProviderSettingsInput,
  WebhookDelivery,
  WebhookSubscription,
} from "./types";

// ---- comments ----

export function listComments(taskId: string) {
  return apiClient.get<Comment[]>(`/tasks/${taskId}/comments`);
}

export function addComment({ taskId, body, parentId, mentionUserIds }: AddCommentInput, options?: ApiRequestOptions) {
  // The author is inferred from the bearer token; never sent by the client.
  return apiClient.post<Comment>(
    `/tasks/${taskId}/comments`,
    {
      body,
      parentId: parentId ?? null,
      mentionUserIds: mentionUserIds ?? [],
    },
    options,
  );
}

export function editComment(id: string, body: string) {
  return apiClient.patch<Comment>(`/comments/${id}`, { body });
}

export function deleteComment(id: string) {
  return apiClient.delete<void>(`/comments/${id}`);
}

export function addReaction(id: string, emoji: string) {
  return apiClient.post<Comment>(`/comments/${id}/reactions`, { emoji });
}

export function removeReaction(id: string, emoji: string) {
  return apiClient.delete<Comment>(`/comments/${id}/reactions/${encodeURIComponent(emoji)}`);
}

// ---- notifications ----

export function listNotifications({ unreadOnly, limit }: ListNotificationsInput = {}) {
  const query = new URLSearchParams();
  if (unreadOnly) query.set("unreadOnly", "true");
  if (limit) query.set("limit", String(limit));
  const suffix = query.toString() ? `?${query}` : "";
  return apiClient.get<Notification[]>(`/notifications${suffix}`);
}

export async function unreadCount() {
  const result = await apiClient.get<{ count: number }>("/notifications/unread-count");
  return result.count;
}

export function markRead(id: string) {
  return apiClient.post<void>(`/notifications/${id}/read`);
}

export function markAllRead() {
  return apiClient.post<void>("/notifications/read-all");
}

export function getPreferences() {
  return apiClient.get<NotificationPreference[]>("/notification-preferences");
}

export function setPreference(eventType: string, patch: PreferencePatch) {
  return apiClient.put<NotificationPreference>(
    `/notification-preferences/${encodeURIComponent(eventType)}`,
    patch,
  );
}

export function getDigestPreference() {
  return apiClient.get<DigestPreference>("/notification-preferences/digest");
}

export function setDigestPreference(frequency: DigestFrequency) {
  return apiClient.put<DigestPreference>("/notification-preferences/digest", { frequency });
}

// ---- presence ----

export async function getPresence(workspaceId: string) {
  const result = await apiClient.get<{ userIds: string[] }>(`/workspaces/${workspaceId}/presence`);
  return result.userIds;
}

// ---- share links ----

export function createShare(
  taskId: string,
  expiresInDays?: number,
  password?: string,
  permissionLevel?: SharePermissionLevel,
) {
  return apiClient.post<ShareLink>(`/tasks/${taskId}/share`, {
    expiresInDays: expiresInDays ?? null,
    password: password || null,
    permissionLevel: permissionLevel ?? null,
  });
}

export function listShares(taskId: string) {
  return apiClient.get<ShareLink[]>(`/tasks/${taskId}/shares`);
}

export function revokeShare(id: string) {
  return apiClient.delete<void>(`/shares/${id}`);
}

export function listShareComments(shareId: string) {
  return apiClient.get<PublicComment[]>(`/shares/${shareId}/comments`);
}

export function listShareAccessLog(shareId: string) {
  return apiClient.get<ShareAccessLogEntry[]>(`/shares/${shareId}/access-log`);
}

// ---- anonymous public surface ----
// The BFF proxy 401s without a session, so server components read these straight from the API origin.

const PUBLIC_API_BASE_URL = (
  process.env.API_BASE_URL ??
  process.env.NEXT_PUBLIC_API_BASE_URL ??
  "http://localhost:8080"
).replace(/\/$/, "");

async function getPublic<T>(path: string): Promise<T | null> {
  const response = await fetch(`${PUBLIC_API_BASE_URL}/api/v1${path}`, {
    headers: { Accept: "application/json" },
    cache: "no-store",
  });
  return response.ok ? ((await response.json()) as T) : null;
}

export function getPublicSharedTask(token: string) {
  return getPublic<SharedTask>(`/public/tasks/${encodeURIComponent(token)}`);
}

/**
 * Browser-side submit for a Comment-level public link. Goes through the BFF proxy (like
 * submitPublicForm below) since the API registers no CORS policy for a direct cross-origin POST.
 */
export function submitPublicComment(token: string, body: string, guestName?: string, password?: string) {
  return apiClient.post<PublicComment>(`/public/tasks/${encodeURIComponent(token)}/comments`, {
    body,
    guestName: guestName || null,
    password: password || null,
  });
}

export function getPublicForm(token: string) {
  return getPublic<PublicForm>(`/public/forms/${encodeURIComponent(token)}`);
}

/**
 * Browser-side submit. Goes through the BFF proxy (which passes `public/*` through without a
 * session) because the API registers no CORS policy — a direct cross-origin POST would be blocked.
 *
 * `honeypot` should always be sent empty (a real visitor never sees
 * or fills that field — see PublicFormPageClient) and `renderedAtUtc` is the timestamp the form was
 * first rendered, so the server can reject implausibly-fast bot submissions.
 */
export function submitPublicForm(
  token: string,
  values: Record<string, string>,
  honeypot?: string,
  renderedAtUtc?: string,
) {
  return apiClient.post<FormSubmitResult>(
    `/public/forms/${encodeURIComponent(token)}/submissions`,
    { values, honeypot: honeypot || null, renderedAtUtc: renderedAtUtc ?? null },
  );
}

/**  . uc(u)ploads a File Upload field's file before the surrounding submission — the
 * returned uploadId is what gets sent as that field's value in submitPublicForm's `values`. */
export function uploadPublicFormFile(token: string, file: File) {
  const body = new FormData();
  body.append("file", file);
  return apiClient.post<FormUploadResult>(`/public/forms/${encodeURIComponent(token)}/uploads`, body);
}

// ---- documents ----

export function listDocuments() {
  return apiClient.get<DocumentSummary[]>("/documents");
}

export function getDocument(id: string) {
  return apiClient.get<Document>(`/documents/${id}`);
}

export function createDocument(input: CreateDocumentInput) {
  return apiClient.post<Document>("/documents", {
    title: input.title,
    content: input.content ?? "",
    isPrivate: input.isPrivate,
    spaceId: input.spaceId ?? null,
    listId: input.listId ?? null,
    taskId: input.taskId ?? null,
    parentDocumentId: input.parentDocumentId ?? null,
    templateId: input.templateId ?? null,
  });
}

export function updateDocument(id: string, input: UpdateDocumentInput) {
  return apiClient.patch<Document>(`/documents/${id}`, input);
}

export function deleteDocument(id: string) {
  return apiClient.delete<void>(`/documents/${id}`);
}

export function getDocumentVersions(id: string) {
  return apiClient.get<DocumentVersion[]>(`/documents/${id}/versions`);
}

export function revertDocument(id: string, versionId: string) {
  return apiClient.post<Document>(`/documents/${id}/revert/${versionId}`);
}

/**: re-parent a document in the wiki tree (server-side cycle-checked). */
export function setDocumentParent(id: string, parentDocumentId: string | null) {
  return apiClient.post<Document>(`/documents/${id}/parent`, { parentDocumentId });
}

/**: Markdown export — returns the rendered text/markdown body. Goes through apiClient (not a
 * bare fetch) so the workspace header rides along, same as every other authenticated call. */
export function exportDocumentMarkdown(id: string) {
  return apiClient.get<string>(`/documents/${id}/export`);
}

// ---- document templates ----

export function listDocumentTemplates() {
  return apiClient.get<DocumentTemplate[]>("/document-templates");
}

export function createDocumentTemplate(documentId: string, name: string) {
  return apiClient.post<DocumentTemplate>(`/document-templates/from-document/${documentId}`, { name });
}

// ---- whiteboards ----

export function listWhiteboards() {
  return apiClient.get<Whiteboard[]>("/whiteboards");
}

export function getWhiteboard(id: string) {
  return apiClient.get<Whiteboard>(`/whiteboards/${id}`);
}

export function createWhiteboard(input: {
  name: string;
  isPrivate: boolean;
  linkedResourceType?: LinkedResourceType | null;
  linkedResourceId?: string | null;
  templateId?: string | null;
}) {
  return apiClient.post<Whiteboard>("/whiteboards", {
    name: input.name,
    isPrivate: input.isPrivate,
    linkedResourceType: input.linkedResourceType ?? null,
    linkedResourceId: input.linkedResourceId ?? null,
    templateId: input.templateId ?? null,
  });
}

export function updateWhiteboard(id: string, input: { name?: string; isPrivate?: boolean }) {
  return apiClient.patch<Whiteboard>(`/whiteboards/${id}`, input);
}

export function archiveWhiteboard(id: string) {
  return apiClient.post<void>(`/whiteboards/${id}/archive`);
}

export function deleteWhiteboard(id: string) {
  return apiClient.delete<void>(`/whiteboards/${id}`);
}

export function listWhiteboardTemplates() {
  return apiClient.get<WhiteboardTemplate[]>("/whiteboard-templates");
}

export function createWhiteboardTemplate(whiteboardId: string, name: string) {
  return apiClient.post<WhiteboardTemplate>(`/whiteboard-templates/from-whiteboard/${whiteboardId}`, { name });
}

export async function uploadWhiteboardImage(whiteboardId: string, file: File | Blob) {
  const body = new FormData();
  body.append("file", file);
  return apiClient.post<{ imageId: string; contentType: string }, FormData>(`/whiteboards/${whiteboardId}/images`, body);
}

/** Plain `<img>`/authenticated-fetch target — the proxy re-applies the workspace header (mirrors
 * attachmentDownloadHref). */
export function whiteboardImageHref(whiteboardId: string, imageId: string) {
  return proxyHref(`/whiteboards/${whiteboardId}/images/${imageId}`);
}

// ---- clips ----

export function listClips() {
  return apiClient.get<Clip[]>("/clips");
}

export function getClip(id: string) {
  return apiClient.get<Clip>(`/clips/${id}`);
}

export type UploadClipInput = {
  title: string;
  description?: string;
  isPrivate?: boolean;
  linkedResourceType?: LinkedResourceType | null;
  linkedResourceId?: string | null;
  durationSeconds?: number | null;
  file: File | Blob;
  fileName?: string;
};

/** Simple-type metadata rides the query string, the file is the multipart body — same convention as
 * WorkManagement's import upload (ASP.NET minimal-API form binding needs an explicit [FromForm] for
 * non-file parameters, so this mirrors the endpoint's actual binding source, see ClipEndpoints.cs). */
export function uploadClip(input: UploadClipInput) {
  const params = new URLSearchParams({ title: input.title, isPrivate: String(input.isPrivate ?? false) });
  if (input.description) params.set("description", input.description);
  if (input.linkedResourceType) params.set("linkedResourceType", input.linkedResourceType);
  if (input.linkedResourceId) params.set("linkedResourceId", input.linkedResourceId);
  if (input.durationSeconds != null) params.set("durationSeconds", String(input.durationSeconds));

  const body = new FormData();
  body.append("file", input.file, input.fileName ?? "clip.webm");
  return apiClient.post<Clip, FormData>(`/clips?${params.toString()}`, body);
}

export function updateClip(id: string, input: { title?: string; description?: string; isPrivate?: boolean }) {
  return apiClient.patch<Clip>(`/clips/${id}`, input);
}

export function deleteClip(id: string) {
  return apiClient.delete<void>(`/clips/${id}`);
}

/** Plain `<video>`/`<audio>` src / download href — same proxy pattern as attachmentDownloadHref. */
export function clipDownloadHref(id: string) {
  return proxyHref(`/clips/${id}/download`);
}

export function listClipComments(clipId: string) {
  return apiClient.get<ClipComment[]>(`/clips/${clipId}/comments`);
}

export function addClipComment(clipId: string, body: string) {
  return apiClient.post<ClipComment>(`/clips/${clipId}/comments`, { body });
}

export function getClipTranscript(clipId: string) {
  return apiClient.get<ClipTranscript | null>(`/clips/${clipId}/transcript`).catch((error) => {
    if (error instanceof ApiError && error.status === 404) return null;
    throw error;
  });
}

export function requestClipTranscript(clipId: string) {
  return apiClient.post<ClipTranscript>(`/clips/${clipId}/transcript`);
}

// ---- forms ----

export function listForms() {
  return apiClient.get<Form[]>("/forms");
}

export function getForm(id: string) {
  return apiClient.get<Form>(`/forms/${id}`);
}

export function createForm(input: CreateFormInput) {
  return apiClient.post<Form>("/forms", input);
}

export function updateForm(id: string, input: UpdateFormInput) {
  return apiClient.patch<Form>(`/forms/${id}`, input);
}

/**  . uc(b)randing/spam-threshold/submission-limits/confirmation-page/full-routing settings. */
export function updateFormSettings(id: string, input: UpdateFormSettingsInput) {
  return apiClient.patch<Form>(`/forms/${id}/settings`, input);
}

export function deleteForm(id: string) {
  return apiClient.delete<void>(`/forms/${id}`);
}

export function getFormSubmissions(id: string) {
  return apiClient.get<FormSubmission[]>(`/forms/${id}/submissions`);
}

/**  . uc(C)SV/Excel export hrefs — same Member+ access control as the builder itself, since
 * these routes go through the authenticated /forms group, not /public/forms. */
export function exportFormSubmissionsCsvHref(id: string) {
  return proxyHref(`/forms/${id}/submissions/export.csv`);
}

export function exportFormSubmissionsXlsxHref(id: string) {
  return proxyHref(`/forms/${id}/submissions/export.xlsx`);
}

// ---- automations ----

export function listAutomations() {
  return apiClient.get<AutomationRule[]>("/automations");
}

export function createAutomation(input: CreateAutomationInput) {
  return apiClient.post<AutomationRule>("/automations", input);
}

export function updateAutomation(id: string, input: UpdateAutomationInput) {
  return apiClient.patch<AutomationRule>(`/automations/${id}`, input);
}

export function setAutomationEnabled(id: string, enabled: boolean) {
  return apiClient.post<AutomationRule>(`/automations/${id}/${enabled ? "enable" : "disable"}`);
}

export function deleteAutomation(id: string) {
  return apiClient.delete<void>(`/automations/${id}`);
}

export function getAutomationRuns(id: string) {
  return apiClient.get<AutomationRun[]>(`/automations/${id}/runs`);
}

// ---- webhooks ----

export function listWebhooks() {
  return apiClient.get<WebhookSubscription[]>("/webhooks");
}

export function createWebhook(input: CreateWebhookInput) {
  return apiClient.post<CreatedWebhook>("/webhooks", input);
}

export function deleteWebhook(id: string) {
  return apiClient.delete<void>(`/webhooks/${id}`);
}

export function getWebhookDeliveries(id: string) {
  return apiClient.get<WebhookDelivery[]>(`/webhooks/${id}/deliveries`);
}

// ---- personal access tokens ----

export function listTokens() {
  return apiClient.get<PersonalAccessToken[]>("/tokens");
}

export function createToken(input: CreateTokenInput) {
  return apiClient.post<CreatedToken>("/tokens", {
    name: input.name,
    scopes: input.scopes,
    expiresAtUtc: input.expiresAtUtc ?? null,
  });
}

export function deleteToken(id: string) {
  return apiClient.delete<void>(`/tokens/${id}`);
}

// ---- OAuth applications ----

export function listOAuthApplications() {
  return apiClient.get<OAuthApplication[]>("/oauth-applications");
}

export function createOAuthApplication(input: CreateOAuthApplicationInput) {
  return apiClient.post<CreatedOAuthApplication>("/oauth-applications", input);
}

export function revokeOAuthApplication(id: string) {
  return apiClient.delete<void>(`/oauth-applications/${id}`);
}

// ---- Third-party integration provider settings ----

export function listProviderSettings() {
  return apiClient.get<IntegrationProviderSettings[]>("/integrations/providers");
}

export function updateProviderSettings(provider: string, input: UpdateProviderSettingsInput) {
  return apiClient.put<IntegrationProviderSettings>(`/integrations/providers/${provider}`, input);
}

// ---- Bulk data importers ----

export function listImportSources() {
  return apiClient.get<string[]>("/imports/sources");
}

export function listImportJobs() {
  return apiClient.get<ImportJob[]>("/imports");
}

export function getImportJob(id: string) {
  return apiClient.get<ImportJob>(`/imports/${id}`);
}

export function listImportJobRows(id: string) {
  return apiClient.get<ImportJobRow[]>(`/imports/${id}/rows`);
}

export function uploadImportJob(input: {
  sourceType: string;
  file: File;
  targetSpaceName?: string;
  targetListName?: string;
}) {
  const body = new FormData();
  body.append("file", input.file);
  const query = new URLSearchParams({ sourceType: input.sourceType });
  if (input.targetSpaceName) query.set("targetSpaceName", input.targetSpaceName);
  if (input.targetListName) query.set("targetListName", input.targetListName);
  return apiClient.post<ImportJob, FormData>(`/imports?${query}`, body);
}

export function setImportMapping(id: string, mapping: Record<string, string>) {
  return apiClient.put<ImportJob>(`/imports/${id}/mapping`, { mapping });
}

export function validateImportJob(id: string) {
  return apiClient.post<ImportJob>(`/imports/${id}/validate`);
}

export function commitImportJob(id: string) {
  return apiClient.post<ImportJob>(`/imports/${id}/commit`);
}
