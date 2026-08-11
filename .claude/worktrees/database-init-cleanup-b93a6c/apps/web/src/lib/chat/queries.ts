// Chat is workspace-scoped; the workspace id keeps caches isolated across workspace switches.
// Realtime updates arrive over the SignalR hub at /hubs/workspace as "entityChanged" events with
// EntityType "ChatMessage" / "ChatChannel"; the realtime client invalidates these roots.
export const chatKeys = {
  all: ["chat"] as const,
  channels: (workspaceId: string) => [...chatKeys.all, "channels", workspaceId] as const,
  channel: (workspaceId: string, channelId: string) =>
    [...chatKeys.channels(workspaceId), channelId] as const,
  messagesRoot: (workspaceId: string) => [...chatKeys.all, "messages", workspaceId] as const,
  messages: (workspaceId: string, channelId: string, before?: string) =>
    [...chatKeys.messagesRoot(workspaceId), channelId, { before: before ?? null }] as const,
};
