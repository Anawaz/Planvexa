// Shapes mirror the backend contracts:
//   Collaboration/Application/Contracts.cs, Notifications/Application/NotificationInboxService.cs,
//   Collaboration/Application/IShareLinkStore.cs, Documents|Forms|Automations|Integrations Contracts.cs

export type CommentReaction = {
  emoji: string;
  userIds: string[];
};

export type Comment = {
  id: string;
  taskId: string;
  parentId?: string | null;
  authorUserId: string;
  body: string;
  isEdited: boolean;
  isDeleted: boolean;
  mentionUserIds: string[];
  reactions: CommentReaction[];
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  replies: Comment[];
};

export type Notification = {
  id: string;
  eventType: string;
  entityType: string;
  entityId: string;
  workspaceId: string;
  payload?: Record<string, string> | null;
  createdAtUtc: string;
  readAtUtc?: string | null;
};

export type NotificationPreference = {
  eventType: string;
  inbox: boolean;
  email: boolean;
  push: boolean;
};

export type PreferencePatch = Pick<NotificationPreference, "inbox" | "email" | "push">;

export type DigestFrequency = "Off" | "Daily" | "Weekly";

export type DigestPreference = {
  frequency: DigestFrequency;
  lastSentAtUtc?: string | null;
};

/**
 * Event types the API publishes today (`NotificationRequest.EventType`). Preferences are stored
 * sparsely server-side (absent = both channels on), so the settings page renders this list merged
 * with whatever the API returns.
 */
export const NOTIFICATION_EVENT_TYPES = ["mention", "automation", "time.missing_time_reminder"] as const;

export const NOTIFICATION_EVENT_LABELS: Record<string, string> = {
  mention: "Comment mentions",
  automation: "Automation notifications",
  "time.missing_time_reminder": "Missing time reminders",
};

export type SharePermissionLevel = "View" | "Comment";

export type ShareLink = {
  id: string;
  taskId: string;
  token: string;
  url: string;
  expiresAtUtc?: string | null;
  requiresPassword: boolean;
  permissionLevel: SharePermissionLevel;
};

/** Anonymous projection returned by GET /public/tasks/{token}. */
export type SharedTask = {
  taskId: string;
  title: string;
  description?: string | null;
  isCompleted: boolean;
  allowsComments: boolean;
};

/** A guest comment left through a Comment-level public link. */
export type PublicComment = {
  id: string;
  guestName?: string | null;
  body: string;
  createdAtUtc: string;
  ipAddress?: string | null;
};

/** One entry in a share link's access log (GET /shares/{id}/access-log). */
export type ShareAccessLogEntry = {
  id: string;
  action: string;
  createdAtUtc: string;
  ipAddress?: string | null;
};

export type ListNotificationsInput = {
  unreadOnly?: boolean;
  limit?: number;
};

export type AddCommentInput = {
  taskId: string;
  body: string;
  parentId?: string | null;
  mentionUserIds?: string[];
};

export type Document = {
  id: string;
  title: string;
  content: string;
  isPrivate: boolean;
  ownerUserId: string;
  spaceId?: string | null;
  listId?: string | null;
  taskId?: string | null;
  parentDocumentId?: string | null;
  updatedAtUtc: string;
};

export type DocumentTemplate = {
  id: string;
  name: string;
  createdAtUtc: string;
};

/** GET /documents returns summaries — no content. */
export type DocumentSummary = Omit<Document, "content">;

export type DocumentVersion = {
  id: string;
  authorUserId: string;
  createdAtUtc: string;
  contentPreview: string;
};

// ---- Whiteboards ----

export type LinkedResourceType = "task" | "document";

export type Whiteboard = {
  id: string;
  name: string;
  isPrivate: boolean;
  ownerUserId: string;
  linkedResourceType?: LinkedResourceType | null;
  linkedResourceId?: string | null;
  isArchived: boolean;
  updatedAtUtc: string;
};

export type WhiteboardTemplate = {
  id: string;
  name: string;
  createdAtUtc: string;
};

export type WhiteboardCollaborationAccess = {
  allowed: boolean;
  canEdit: boolean;
  userId: string | null;
};

// ---- Clips ----

export type ClipStatus = "Recording" | "Processing" | "Ready" | "Failed";

export type Clip = {
  id: string;
  title: string;
  description?: string | null;
  isPrivate: boolean;
  ownerUserId: string;
  linkedResourceType?: LinkedResourceType | null;
  linkedResourceId?: string | null;
  contentType: string;
  sizeBytes: number;
  durationSeconds?: number | null;
  status: ClipStatus;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type ClipComment = {
  id: string;
  authorUserId: string;
  body: string;
  createdAtUtc: string;
};

export type ClipTranscriptStatus = "Unavailable" | "Pending" | "Ready" | "Failed";

export type ClipTranscriptSegment = {
  startSeconds: number;
  endSeconds: number;
  text: string;
};

export type ClipTranscript = {
  status: ClipTranscriptStatus;
  text?: string | null;
  segments?: ClipTranscriptSegment[] | null;
  updatedAtUtc: string;
};

export type FormFieldType =
  | "Text"
  | "LongText"
  | "Number"
  | "Date"
  | "Select"
  | "FileUpload"
  | "Boolean"
  | "Email"
  | "Phone"
  | "Url";

/** Form.Domain.FormFieldConditionOperator on the backend. */
export type FormFieldConditionOperator = "Equals" | "NotEquals" | "Contains" | "IsEmpty" | "IsNotEmpty";

export type FormFieldDef = {
  id: string;
  label: string;
  type: FormFieldType;
  required: boolean;
  options: string[];
  position: number;
  conditionFieldId?: string | null;
  conditionOperator?: FormFieldConditionOperator | null;
  conditionValue?: string | null;
  customFieldDefinitionId?: string | null;
};

/** The public projection's field shape omits customFieldDefinitionId (internal routing, never public). */
export type PublicFormFieldDef = Omit<FormFieldDef, "customFieldDefinitionId">;

export type Form = {
  id: string;
  listId: string;
  title: string;
  description?: string | null;
  isActive: boolean;
  publicToken: string;
  fields: FormFieldDef[];
  brandingLogoUrl?: string | null;
  brandingColor?: string | null;
  confirmationMessage?: string | null;
  confirmationRedirectUrl?: string | null;
  minSubmitSeconds?: number | null;
  maxTotalSubmissions?: number | null;
  maxSubmissionsPerRespondent?: number | null;
  targetStatusName?: string | null;
  targetPriority?: string | null;
  targetTags: string[];
  targetTeamId?: string | null;
  dueDateDaysAfterSubmission?: number | null;
};

/** Anonymous projection returned by GET /public/forms/{token}. */
export type PublicForm = {
  title: string;
  description?: string | null;
  fields: PublicFormFieldDef[];
  brandingLogoUrl?: string | null;
  brandingColor?: string | null;
  confirmationMessage?: string | null;
  confirmationRedirectUrl?: string | null;
};

export type FormSubmission = {
  id: string;
  createdTaskId?: string | null;
  submittedAtUtc: string;
  values: Record<string, string>;
};

export type FormSubmitResult = {
  submissionId: string;
  createdTaskId?: string | null;
};

export type FormUploadResult = {
  uploadId: string;
  fileName: string;
  sizeBytes: number;
};

export type AutomationRule = {
  id: string;
  name: string;
  triggerType: string;
  isEnabled: boolean;
  conditionJson: string;
  actionJson: string;
  /**  . uc(s)cheduled/due-date trigger config (e.g. `{"everyMinutes":60}`); unused by other triggers. */
  triggerConfigJson?: string | null;
  /**  . uc(i)ncremented on every edit; see the rule's /versions history. */
  version?: number;
};

/** WorkspaceEvent.Types on the backend. */
export const AUTOMATION_TRIGGER_TYPES = [
  "task.created",
  "task.status_changed",
  "task.assigned",
  "task.completed",
  "form.submitted",
  "comment.created",
  "time_entry.logged",
  "task.due_soon",
  "schedule.recurring",
  "task.sla_breached",
] as const;

export type AutomationRun = {
  id: string;
  ruleId: string;
  status: string;
  detail?: string | null;
  occurredAtUtc: string;
};

export type WebhookSubscription = {
  id: string;
  url: string;
  eventTypes: string[];
  isActive: boolean;
  createdAtUtc: string;
};

/** POST /webhooks additionally returns the signing secret, shown once. */
export type CreatedWebhook = WebhookSubscription & { secret: string };

export type WebhookDelivery = {
  id: string;
  eventType: string;
  attempt: number;
  success: boolean;
  statusCode?: number | null;
  detail?: string | null;
  occurredAtUtc: string;
};

export type PersonalAccessToken = {
  id: string;
  name: string;
  scopes: string[];
  lastUsedAtUtc?: string | null;
  expiresAtUtc?: string | null;
  createdAtUtc: string;
};

/** POST /tokens additionally returns the raw token, shown once. */
export type CreatedToken = Omit<PersonalAccessToken, "lastUsedAtUtc"> & { token: string };

export type CreateDocumentInput = {
  title: string;
  content?: string;
  isPrivate: boolean;
  spaceId?: string | null;
  listId?: string | null;
  taskId?: string | null;
  parentDocumentId?: string | null;
  templateId?: string | null;
};

export type UpdateDocumentInput = {
  title?: string;
  content?: string;
  isPrivate?: boolean;
};

export type FormFieldInput = Omit<FormFieldDef, "id"> & { id?: string };

export type CreateFormInput = {
  listId: string;
  title: string;
  description?: string | null;
  fields: FormFieldInput[];
};

export type UpdateFormInput = {
  title?: string;
  description?: string | null;
  isActive?: boolean;
  fields?: FormFieldInput[];
};

export type UpdateFormSettingsInput = {
  brandingLogoUrl?: string | null;
  brandingColor?: string | null;
  confirmationMessage?: string | null;
  confirmationRedirectUrl?: string | null;
  minSubmitSeconds?: number | null;
  maxTotalSubmissions?: number | null;
  maxSubmissionsPerRespondent?: number | null;
  targetStatusName?: string | null;
  targetPriority?: string | null;
  targetTagsCsv?: string | null;
  targetTeamId?: string | null;
  dueDateDaysAfterSubmission?: number | null;
};

export type CreateAutomationInput = {
  name: string;
  triggerType: string;
  conditionJson?: string | null;
  actionJson?: string | null;
};

export type UpdateAutomationInput = Partial<CreateAutomationInput>;

export type CreateWebhookInput = {
  url: string;
  eventTypes: string[];
};

export type CreateTokenInput = {
  name: string;
  scopes: string[];
  expiresAtUtc?: string | null;
};

// ---- OAuth applications ----

export type OAuthApplication = {
  id: string;
  name: string;
  clientId: string;
  redirectUris: string[];
  allowedScopes: string[];
  isActive: boolean;
  createdAtUtc: string;
};

/** POST /oauth-applications additionally returns the raw client secret, shown once. */
export type CreatedOAuthApplication = OAuthApplication & { clientSecret: string };

export type CreateOAuthApplicationInput = {
  name: string;
  redirectUris: string[];
  allowedScopes: string[];
};

// ---- third-party integration provider settings ----

export type IntegrationProviderSettings = {
  provider: string;
  configJson: string;
  secretHint: string;
  isEnabled: boolean;
  hasRealImplementation: boolean;
};

export type UpdateProviderSettingsInput = {
  configJson: string;
  secret?: string | null;
  isEnabled: boolean;
};

// ---- bulk data importers ----

export type ImportJob = {
  id: string;
  sourceType: string;
  fileName: string;
  status: string;
  detectedColumns: string[];
  columnMappingJson?: string | null;
  targetSpaceName?: string | null;
  targetListName?: string | null;
  totalRows: number;
  committedRows: number;
  errorCount: number;
  createdAtUtc: string;
};

export type ImportJobRow = {
  id: string;
  rowIndex: number;
  status: string;
  errorMessage?: string | null;
  createdTaskId?: string | null;
};
