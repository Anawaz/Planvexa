import { apiClient } from "@/lib/api-client";
import type {
  AiPrioritySuggestion,
  AiProviderSettings,
  AiProviderTestResult,
  AiSubtaskSuggestion,
  AiSummary,
  AiUsage,
  Device,
  RegisterDeviceInput,
  RetentionPolicy,
  SyncResult,
  UpdateAiProviderSettingsInput,
  UpdateRetentionPolicyInput,
} from "./types";

// ---- AI assist ----

export function summarizeTask(taskId: string) {
  return apiClient.post<AiSummary>(`/ai/tasks/${taskId}/summarize`);
}

export function suggestSubtasks(taskId: string) {
  return apiClient.post<AiSubtaskSuggestion>(`/ai/tasks/${taskId}/subtasks`);
}

export function suggestPriority(taskId: string) {
  return apiClient.post<AiPrioritySuggestion>(`/ai/tasks/${taskId}/priority`);
}

export function getAiUsage() {
  return apiClient.get<AiUsage>("/ai/usage");
}

// ---- AI provider settings (Admin+) ----

export function getAiProviderSettings() {
  return apiClient.get<AiProviderSettings>("/ai/settings");
}

export function updateAiProviderSettings(input: UpdateAiProviderSettingsInput) {
  return apiClient.put<AiProviderSettings>("/ai/settings", { ...input, apiKey: input.apiKey ?? null });
}

export function testAiProviderSettings(input: UpdateAiProviderSettingsInput) {
  return apiClient.post<AiProviderTestResult>("/ai/settings/test", { ...input, apiKey: input.apiKey ?? null });
}

// ---- mobile devices ----

export function listDevices() {
  return apiClient.get<Device[]>("/mobile/devices");
}

export function registerDevice(input: RegisterDeviceInput) {
  return apiClient.post<Device>("/mobile/devices", {
    platform: input.platform,
    pushToken: input.pushToken,
    appVersion: input.appVersion ?? null,
    endpoint: input.endpoint,
    p256dh: input.p256dh,
    auth: input.auth,
  });
}

/** The backend's ephemeral (regenerates on restart — dev scope) VAPID public key, needed by
 * `PushManager.subscribe({ applicationServerKey })`. See LoggingPushSender.cs's doc comment.
 * Defensive about the exact wire shape (a bare JSON string vs. `{ key }`/`{ publicKey }`) since it's
 * a tiny, easy-to-bikeshed endpoint. */
export async function getVapidPublicKey(): Promise<string> {
  const response = await apiClient.get<string | { key?: string; publicKey?: string }>("/mobile/push/vapid-public-key");
  if (typeof response === "string") return response;
  const key = response.key ?? response.publicKey;
  if (!key) throw new Error("The server did not return a VAPID public key.");
  return key;
}

export function unregisterDevice(id: string) {
  return apiClient.delete<void>(`/mobile/devices/${id}`);
}

export function sync(sinceUtc?: string) {
  const suffix = sinceUtc ? `?since=${encodeURIComponent(sinceUtc)}` : "";
  return apiClient.get<SyncResult>(`/mobile/sync${suffix}`);
}

// ---- data retention ----

export function getRetentionPolicy() {
  return apiClient.get<RetentionPolicy>("/governance/retention-policy");
}

export function updateRetentionPolicy(input: UpdateRetentionPolicyInput) {
  return apiClient.put<RetentionPolicy>("/governance/retention-policy", input);
}
