export type IsoDateString = string;

export type ChatChannelType = "Workspace" | "Space" | "List" | "Task" | "Private" | "Dm" | "GroupDm";

export type ChatLinkedResourceType = "space" | "list" | "task";

export type ChatChannel = {
  id: string;
  channelType: ChatChannelType;
  name: string;
  description?: string | null;
  isPrivate: boolean;
  isArchived: boolean;
  linkedResourceType?: ChatLinkedResourceType | null;
  linkedResourceId?: string | null;
  createdByUserId: string;
  createdAtUtc: IsoDateString;
  memberUserIds: string[];
};

export type ChatChannelSummary = {
  id: string;
  channelType: ChatChannelType;
  name: string;
  description?: string | null;
  isPrivate: boolean;
  isArchived: boolean;
  linkedResourceType?: ChatLinkedResourceType | null;
  linkedResourceId?: string | null;
  createdAtUtc: IsoDateString;
  memberUserIds: string[];
  unreadCount: number;
};

export type ChatReaction = {
  emoji: string;
  userIds: string[];
};

export type ChatAttachment = {
  id: string;
  messageId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedByUserId: string;
  createdAtUtc: IsoDateString;
};

export type ChatMessage = {
  id: string;
  channelId: string;
  parentMessageId?: string | null;
  authorUserId: string;
  body: string;
  isDeleted: boolean;
  createdAtUtc: IsoDateString;
  editedAtUtc?: IsoDateString | null;
  mentionUserIds: string[];
  reactions: ChatReaction[];
  attachments: ChatAttachment[];
};

export type CreateChatChannelInput = {
  name: string;
  description?: string | null;
  isPrivate: boolean;
  memberUserIds?: string[];
};

export type CreateLinkedChatChannelInput = {
  linkedResourceType: ChatLinkedResourceType;
  linkedResourceId: string;
  name: string;
  description?: string | null;
};

export type CreateDirectMessageInput = {
  participantUserIds: string[];
};

export type UpdateChatChannelInput = {
  name: string;
  description?: string | null;
};

export type PostChatMessageInput = {
  parentMessageId?: string | null;
  body: string;
  mentionUserIds?: string[];
};

export type EditChatMessageInput = {
  body: string;
};
