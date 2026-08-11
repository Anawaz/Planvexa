export type AiSummary = {
  taskId: string;
  summary: string;
  tokensEstimated: number;
};

export type AiSubtaskSuggestion = {
  titles: string[];
  tokensEstimated: number;
};

export type AiPriority = "None" | "Low" | "Normal" | "High" | "Urgent";

export type AiPrioritySuggestion = {
  /** Domain enum name; matches AiPriority in practice, kept loose for forward compatibility. */
  priority: string;
  rationale: string;
  tokensEstimated: number;
};

export type AiUsage = {
  requestCount: number;
  tokensEstimated: number;
  creditsEnabled: boolean;
  creditLimit?: number | null;
};

export type DevicePlatform = "Ios" | "Android" | "Web";

export type Device = {
  id: string;
  platform: DevicePlatform;
  appVersion?: string | null;
  lastSeenAtUtc: string;
  createdAtUtc: string;
};

export type RegisterDeviceInput = {
  platform: DevicePlatform;
  /** Required by the API; stored hashed and never echoed back. */
  pushToken: string;
  appVersion?: string | null;
  /**  . uc(t)he browser `PushSubscription`'s addressing info (Web platform only) — stored raw
   * (unlike `pushToken`) because a real sender needs it to encrypt/deliver a push. See
   * `lib/push/subscribe.ts`. */
  endpoint?: string;
  p256dh?: string;
  auth?: string;
};

export type SyncChange = {
  taskId: string;
  listId: string;
  spaceId: string;
  title: string;
  priority: string;
  isCompleted: boolean;
  isDeleted: boolean;
  dueDate?: string | null;
  changedAtUtc: string;
};

export type SyncResult = {
  changes: SyncChange[];
  nextCursorUtc: string;
};

export type AiProviderSettings = {
  baseUrl: string;
  model: string;
  /** Masked hint only ("•••1234"), never the key. Empty when no key is stored. */
  apiKeyMask: string;
  isEnabled: boolean;
};

export type UpdateAiProviderSettingsInput = {
  baseUrl: string;
  model: string;
  /** Omit or leave blank to keep the stored key. */
  apiKey?: string;
  isEnabled: boolean;
};

export type AiProviderTestResult = {
  ok: boolean;
  message: string;
};

export type RetentionPolicy = {
  deletedTaskRetentionDays: number;
  auditRetentionDays: number;
  legalHold: boolean;
};

export type UpdateRetentionPolicyInput = RetentionPolicy;
