import { apiClient, proxyHref } from "@/lib/api-client";
import type {
  ChatAttachment,
  ChatChannel,
  ChatChannelSummary,
  ChatMessage,
  CreateChatChannelInput,
  CreateDirectMessageInput,
  CreateLinkedChatChannelInput,
  EditChatMessageInput,
  PostChatMessageInput,
  UpdateChatChannelInput,
} from "./types";

export function listChannels() {
  return apiClient.get<ChatChannelSummary[]>("/chat/channels");
}

export function getChannel(id: string) {
  return apiClient.get<ChatChannel>(`/chat/channels/${id}`);
}

export function createChannel(input: CreateChatChannelInput) {
  return apiClient.post<ChatChannel>("/chat/channels", {
    name: input.name,
    description: input.description ?? null,
    isPrivate: input.isPrivate,
    memberUserIds: input.memberUserIds ?? [],
  });
}

export function createLinkedChannel(input: CreateLinkedChatChannelInput) {
  return apiClient.post<ChatChannel>("/chat/channels/linked", {
    linkedResourceType: input.linkedResourceType,
    linkedResourceId: input.linkedResourceId,
    name: input.name,
    description: input.description ?? null,
  });
}

export function createDirectMessage(input: CreateDirectMessageInput) {
  return apiClient.post<ChatChannel>("/chat/channels/direct", {
    participantUserIds: input.participantUserIds,
  });
}

export function updateChannel(id: string, input: UpdateChatChannelInput) {
  return apiClient.patch<ChatChannel>(`/chat/channels/${id}`, {
    name: input.name,
    description: input.description ?? null,
  });
}

export function archiveChannel(id: string) {
  return apiClient.post<void>(`/chat/channels/${id}/archive`);
}

export function addMember(id: string, userId: string) {
  return apiClient.post<ChatChannel>(`/chat/channels/${id}/members`, { userId });
}

export function removeMember(id: string, userId: string) {
  return apiClient.delete<ChatChannel>(`/chat/channels/${id}/members/${userId}`);
}

export function markChannelRead(id: string, lastReadMessageId?: string | null) {
  return apiClient.post<void>(`/chat/channels/${id}/read`, { lastReadMessageId: lastReadMessageId ?? null });
}

export function listMessages(channelId: string, before?: string) {
  const suffix = before ? `?before=${encodeURIComponent(before)}` : "";
  return apiClient.get<ChatMessage[]>(`/chat/channels/${channelId}/messages${suffix}`);
}

export function postMessage(channelId: string, input: PostChatMessageInput) {
  return apiClient.post<ChatMessage>(`/chat/channels/${channelId}/messages`, {
    parentMessageId: input.parentMessageId ?? null,
    body: input.body,
    mentionUserIds: input.mentionUserIds ?? [],
  });
}

export function editMessage(messageId: string, input: EditChatMessageInput) {
  return apiClient.patch<ChatMessage>(`/chat/messages/${messageId}`, { body: input.body });
}

export function deleteMessage(messageId: string) {
  return apiClient.delete<void>(`/chat/messages/${messageId}`);
}

export function addReaction(messageId: string, emoji: string) {
  return apiClient.post<ChatMessage>(`/chat/messages/${messageId}/reactions`, { emoji });
}

export function removeReaction(messageId: string, emoji: string) {
  return apiClient.delete<ChatMessage>(`/chat/messages/${messageId}/reactions/${encodeURIComponent(emoji)}`);
}

export function uploadAttachment(messageId: string, file: File) {
  const body = new FormData();
  body.append("file", file);
  return apiClient.post<ChatAttachment, FormData>(`/chat/messages/${messageId}/attachments`, body);
}

export function deleteAttachment(id: string) {
  return apiClient.delete<void>(`/chat/attachments/${id}`);
}

/** Plain `<a href>` target — the proxy re-applies the workspace header from query params. */
export function chatAttachmentDownloadHref(id: string) {
  return proxyHref(`/chat/attachments/${id}/download`);
}
