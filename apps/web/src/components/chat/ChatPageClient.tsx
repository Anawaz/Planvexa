"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "next/navigation";
import {
  type ChangeEvent,
  type FormEvent,
  type KeyboardEvent,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
} from "react";
import { Avatar } from "@/components/ui/Avatar";
import { AttachmentPreview } from "@/components/ui/AttachmentPreview";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Input } from "@/components/ui/Input";
import { QueryState } from "@/components/ui/QueryState";
import { ResourcePicker } from "@/components/ui/ResourcePicker";
import type { SearchResultType } from "@/lib/search/client";
import {
  addMember,
  addReaction,
  archiveChannel,
  chatAttachmentDownloadHref,
  createChannel,
  createDirectMessage,
  createLinkedChannel,
  deleteMessage,
  editMessage,
  getChannel,
  listChannels,
  listMessages,
  markChannelRead,
  postMessage,
  removeMember,
  removeReaction,
  updateChannel,
  uploadAttachment,
} from "@/lib/chat/client";
import { chatKeys } from "@/lib/chat/queries";
import type {
  ChatChannel,
  ChatChannelSummary,
  ChatChannelType,
  ChatLinkedResourceType,
  ChatMessage,
  CreateChatChannelInput,
  CreateDirectMessageInput,
  CreateLinkedChatChannelInput,
} from "@/lib/chat/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { useFileDropZone } from "@/lib/files/useFileDropZone";
import { useCurrentUserId, useMemberDirectory, useMembers } from "@/lib/members";
import { useRecordRecentView } from "@/lib/recent/useRecordRecentView";
import { useTypingBroadcast } from "@/lib/realtime/useRealtime";
import { TypingIndicator } from "@/components/collab/TypingIndicator";
import { cn } from "@/lib/utils";

type MemberDirectory = ReturnType<typeof useMemberDirectory>;

function linkedResourceTypeToSearchType(type: ChatLinkedResourceType): SearchResultType {
  switch (type) {
    case "space":
      return "Space";
    case "list":
      return "List";
    case "task":
      return "Task";
  }
}

type ThreadedMessage = ChatMessage & {
  replies: ChatMessage[];
};

const QUICK_REACTIONS = ["👍", "❤️", "😂", "🎉", "👀", "🚀"];

const timestampFormatter = new Intl.DateTimeFormat("en", {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

function formatTimestamp(iso: string) {
  return timestampFormatter.format(new Date(iso));
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** DMs/group DMs have no server-side name — synthesize one from the other participant(s). */
function channelDisplayName(
  channel: { channelType: ChatChannelType; name: string; memberUserIds: string[] },
  currentUserId: string | null | undefined,
  directory: MemberDirectory,
): string {
  if (channel.channelType !== "Dm" && channel.channelType !== "GroupDm") {
    return channel.name;
  }

  const others = channel.memberUserIds.filter((id) => id !== currentUserId);
  return others.length > 0 ? others.map((id) => directory.getLabel(id)).join(", ") : "You";
}

function channelKindBadge(channelType: ChatChannelType) {
  switch (channelType) {
    case "Private":
      return { icon: "🔒", label: "Private channel" };
    case "Dm":
      return { icon: "💬", label: "Direct message" };
    case "GroupDm":
      return { icon: "👥", label: "Group direct message" };
    case "Space":
    case "List":
    case "Task":
      return { icon: "🔗", label: `Linked to a ${channelType.toLowerCase()}` };
    default:
      return null;
  }
}

function buildThreads(messages: ChatMessage[]): ThreadedMessage[] {
  const topLevel: ChatMessage[] = [];
  const repliesByParent = new Map<string, ChatMessage[]>();

  messages.forEach((message) => {
    if (!message.parentMessageId) {
      topLevel.push(message);
      return;
    }

    repliesByParent.set(message.parentMessageId, [
      ...(repliesByParent.get(message.parentMessageId) ?? []),
      message,
    ]);
  });

  return topLevel.map((message) => ({
    ...message,
    replies: repliesByParent.get(message.id) ?? [],
  }));
}

type ChannelKind = "workspace" | "private" | "dm" | "linked";

type NewChannelFormProps = {
  isCreating: boolean;
  onCreate: (input: CreateChatChannelInput) => void;
  onCreateLinked: (input: CreateLinkedChatChannelInput) => void;
  onCreateDirectMessage: (input: CreateDirectMessageInput) => void;
};

function NewChannelForm({ isCreating, onCreate, onCreateLinked, onCreateDirectMessage }: NewChannelFormProps) {
  const [kind, setKind] = useState<ChannelKind>("workspace");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [linkedResourceType, setLinkedResourceType] = useState<ChatLinkedResourceType>("list");
  const [linkedResourceId, setLinkedResourceId] = useState("");
  const [participantUserIds, setParticipantUserIds] = useState<string[]>([]);
  const directory = useMemberDirectory();
  const currentUserId = useCurrentUserId();
  const { data: workspaceMembers } = useMembers();
  const otherMembers = (workspaceMembers ?? []).filter((member) => member.userId !== currentUserId);

  function resetFields() {
    setName("");
    setDescription("");
    setLinkedResourceId("");
    setParticipantUserIds([]);
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (isCreating) {
      return;
    }

    if (kind === "dm") {
      if (participantUserIds.length === 0) {
        return;
      }

      onCreateDirectMessage({ participantUserIds });
      resetFields();
      return;
    }

    if (kind === "linked") {
      if (!name.trim() || !linkedResourceId.trim()) {
        return;
      }

      onCreateLinked({ linkedResourceType, linkedResourceId: linkedResourceId.trim(), name: name.trim(), description });
      resetFields();
      return;
    }

    if (!name.trim()) {
      return;
    }

    onCreate({ name, description, isPrivate: kind === "private" });
    resetFields();
  }

  const canSubmit =
    !isCreating &&
    (kind === "dm" ? participantUserIds.length > 0 : kind === "linked" ? Boolean(name.trim() && linkedResourceId.trim()) : Boolean(name.trim()));

  return (
    <form className="space-y-3 rounded-xl border border-border bg-background p-4" onSubmit={handleSubmit}>
      <div>
        <h2 className="text-sm font-semibold">New conversation</h2>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">
          Channels, private channels, direct messages, and resource-linked channels all live in the
          current workspace.
        </p>
      </div>

      <div className="grid gap-2">
        <label htmlFor="chat-channel-kind" className="text-sm font-medium">
          Kind
        </label>
        <select
          id="chat-channel-kind"
          value={kind}
          className="h-10 rounded-lg border border-border bg-background px-3 text-sm shadow-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          onChange={(event) => setKind(event.target.value as ChannelKind)}
          disabled={isCreating}
        >
          <option value="workspace">Workspace channel</option>
          <option value="private">Private channel</option>
          <option value="dm">Direct message</option>
          <option value="linked">Linked to a Space/List/Task</option>
        </select>
      </div>

      {kind === "dm" ? (
        <fieldset className="grid gap-2">
          <legend className="text-sm font-medium">Participants</legend>
          <div className="max-h-40 space-y-1 overflow-y-auto rounded-lg border border-border bg-card p-2" aria-label="Pick teammates">
            {otherMembers.length === 0 ? (
              <p className="px-1 py-1 text-xs text-muted-foreground">No other workspace members yet.</p>
            ) : (
              otherMembers.map((member) => {
                const checked = participantUserIds.includes(member.userId);
                return (
                  <label
                    key={member.userId}
                    className="flex items-center gap-2 rounded-md px-1 py-1 text-sm hover:bg-muted"
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      className="size-4 rounded border-border accent-primary"
                      disabled={isCreating}
                      onChange={() =>
                        setParticipantUserIds((current) =>
                          checked ? current.filter((id) => id !== member.userId) : [...current, member.userId],
                        )
                      }
                    />
                    {directory.getLabel(member.userId)}
                  </label>
                );
              })
            )}
          </div>
          <p className="text-xs text-muted-foreground">
            One other person starts a DM; two or more starts a group DM.
          </p>
        </fieldset>
      ) : null}

      {kind === "linked" ? (
        <div className="grid gap-2">
          <label htmlFor="chat-linked-type" className="text-sm font-medium">
            Resource type
          </label>
          <select
            id="chat-linked-type"
            value={linkedResourceType}
            className="h-10 rounded-lg border border-border bg-background px-3 text-sm shadow-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            onChange={(event) => {
              setLinkedResourceType(event.target.value as ChatLinkedResourceType);
              setLinkedResourceId("");
            }}
            disabled={isCreating}
          >
            <option value="space">Space</option>
            <option value="list">List</option>
            <option value="task">Task</option>
          </select>
          <label htmlFor="chat-linked-id" className="text-sm font-medium">
            {linkedResourceType === "space" ? "Space" : linkedResourceType === "list" ? "List" : "Task"}
          </label>
          <ResourcePicker
            id="chat-linked-id"
            types={[linkedResourceTypeToSearchType(linkedResourceType)]}
            value={linkedResourceId}
            onChange={(id) => setLinkedResourceId(id)}
            placeholder={`Search ${linkedResourceType}s…`}
            disabled={isCreating}
          />
        </div>
      ) : null}

      {kind !== "dm" ? (
        <>
          <Input
            id="chat-channel-name"
            label="Channel name"
            value={name}
            placeholder="e.g. Release room"
            onChange={(event) => setName(event.target.value)}
            disabled={isCreating}
            required
          />
          <div className="grid gap-2">
            <label htmlFor="chat-channel-description" className="text-sm font-medium">
              Description
            </label>
            <textarea
              id="chat-channel-description"
              value={description}
              placeholder="What should teammates discuss here?"
              className="min-h-20 rounded-lg border border-border bg-background px-3 py-2 text-sm shadow-sm outline-none transition placeholder:text-muted-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50 motion-reduce:transition-none"
              onChange={(event) => setDescription(event.target.value)}
              disabled={isCreating}
            />
          </div>
        </>
      ) : null}

      <Button type="submit" size="sm" className="w-full" disabled={!canSubmit}>
        {isCreating ? "Creating…" : kind === "dm" ? "Start conversation" : "Create channel"}
      </Button>
    </form>
  );
}

type ChannelListProps = {
  channels: ChatChannelSummary[];
  isLoading: boolean;
  isError: boolean;
  error?: unknown;
  onRetry: () => void;
  selectedChannelId: string | null;
  currentUserId: string | null | undefined;
  directory: MemberDirectory;
  onSelect: (channelId: string) => void;
};

function ChannelList({
  channels,
  isLoading,
  isError,
  error,
  onRetry,
  selectedChannelId,
  currentUserId,
  directory,
  onSelect,
}: ChannelListProps) {
  if (isLoading) {
    return (
      <div
        className="space-y-2 rounded-xl border border-border bg-background p-4"
        aria-label="Loading channels"
      >
        {Array.from({ length: 3 }, (_, index) => (
          <div
            key={index}
            className="h-12 animate-pulse rounded-lg bg-muted/70"
          />
        ))}
      </div>
    );
  }

  // isLoading is handled above (its own skeleton, not QueryState's plain-text one) — this only
  // needs QueryState's isError branch, with channel-list-shaped copy for the true empty case.
  return (
    <QueryState query={{ isLoading: false, isError, error, refetch: onRetry }}>
      {channels.length === 0 ? (
        <EmptyState
          title="No channels yet"
          description="Create the first workspace channel to start a conversation."
        />
      ) : (
    <ul className="space-y-2" aria-label="Workspace channels">
      {channels.map((channel) => {
        const isSelected = channel.id === selectedChannelId;
        const badge = channelKindBadge(channel.channelType);
        const displayName = channelDisplayName(channel, currentUserId, directory);

        return (
          <li key={channel.id}>
            <button
              type="button"
              aria-pressed={isSelected}
              className={cn(
                "w-full rounded-xl border p-3 text-left transition focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none",
                isSelected
                  ? "border-primary bg-primary/10 text-foreground shadow-sm"
                  : "border-border bg-background text-muted-foreground hover:bg-muted hover:text-foreground",
              )}
              onClick={() => onSelect(channel.id)}
            >
              <span className="flex items-center gap-2 text-sm font-semibold">
                {badge ? (
                  <span aria-label={badge.label} title={badge.label}>
                    {badge.icon}
                  </span>
                ) : null}
                <span className="truncate">{displayName}</span>
                {channel.unreadCount > 0 ? (
                  <span className="rounded-full bg-primary px-2 py-0.5 text-[0.65rem] font-semibold text-primary-foreground">
                    {channel.unreadCount}
                  </span>
                ) : null}
                {channel.isArchived ? (
                  <span className="ml-auto rounded-full bg-muted px-2 py-0.5 text-[0.7rem] font-medium uppercase tracking-wide text-muted-foreground">
                    Archived
                  </span>
                ) : null}
              </span>
              {channel.description ? (
                <span className="mt-2 line-clamp-2 block text-xs leading-5">
                  {channel.description}
                </span>
              ) : null}
            </button>
          </li>
        );
      })}
    </ul>
      )}
    </QueryState>
  );
}

type MessageItemProps = {
  depth: 0 | 1;
  message: ChatMessage;
  isBusy: boolean;
  onDelete: (message: ChatMessage) => void;
  onEdit: (message: ChatMessage, body: string) => void;
  onReply: (parentMessageId: string) => void;
  onToggleReaction: (message: ChatMessage, emoji: string, alreadyReacted: boolean) => void;
};

function MessageItem({ depth, message, isBusy, onDelete, onEdit, onReply, onToggleReaction }: MessageItemProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(message.body);
  const [pickerOpen, setPickerOpen] = useState(false);
  const directory = useMemberDirectory();
  const currentUserId = useCurrentUserId();
  const authorName = directory.getLabel(message.authorUserId);
  const isOwnMessage = Boolean(currentUserId) && message.authorUserId === currentUserId;
  const canChange = isOwnMessage && !message.isDeleted;
  const parentMessageId = message.parentMessageId ?? message.id;

  function cancelEdit() {
    setDraft(message.body);
    setIsEditing(false);
  }

  function submitEdit() {
    const body = draft.trim();

    if (!body) {
      return;
    }

    onEdit(message, body);
    setIsEditing(false);
  }

  function handleEditKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      cancelEdit();
      return;
    }

    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      submitEdit();
    }
  }

  return (
    <article
      className={cn(
        "rounded-xl border border-border bg-card p-4 shadow-sm",
        depth === 1 && "bg-background/80",
      )}
    >
      <header className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <Avatar
          avatarUrl={directory.getAvatarUrl(message.authorUserId)}
          initials={directory.getInitials(message.authorUserId)}
          className="grid size-8 place-items-center rounded-full border border-border bg-background font-semibold text-foreground"
        />
        <span className="font-semibold text-foreground">{authorName}</span>
        <span aria-hidden="true">·</span>
        <time dateTime={message.createdAtUtc}>{formatTimestamp(message.createdAtUtc)}</time>
        {message.editedAtUtc && !message.isDeleted ? <span>(edited)</span> : null}
      </header>

      {isEditing ? (
        <div className="mt-3 space-y-2">
          <label htmlFor={`edit-${message.id}`} className="sr-only">
            Edit message from {authorName}
          </label>
          <textarea
            id={`edit-${message.id}`}
            value={draft}
            className="min-h-24 w-full rounded-lg border border-border bg-background px-3 py-2 text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={handleEditKeyDown}
            autoFocus
          />
          <div className="flex flex-wrap gap-2">
            <Button type="button" size="sm" onClick={submitEdit} disabled={!draft.trim() || isBusy}>
              Save
            </Button>
            <Button type="button" variant="ghost" size="sm" onClick={cancelEdit}>
              Cancel
            </Button>
          </div>
        </div>
      ) : (
        <p
          className={cn(
            "mt-3 whitespace-pre-wrap text-sm leading-6",
            message.isDeleted && "italic text-muted-foreground",
          )}
        >
          {message.isDeleted ? "(message deleted)" : message.body}
        </p>
      )}

      {!message.isDeleted && message.attachments.length > 0 ? (
        <ul className="mt-2 space-y-1" aria-label="Attachments">
          {message.attachments.map((attachment) => (
            <li key={attachment.id} className="space-y-1">
              <a
                href={chatAttachmentDownloadHref(attachment.id)}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1 text-xs font-medium text-primary underline-offset-2 hover:underline"
              >
                📎 {attachment.fileName}
                <span className="text-muted-foreground">({formatBytes(attachment.sizeBytes)})</span>
              </a>
              <AttachmentPreview
                fileName={attachment.fileName}
                contentType={attachment.contentType}
                href={chatAttachmentDownloadHref(attachment.id)}
              />
            </li>
          ))}
        </ul>
      ) : null}

      {!message.isDeleted ? (
        <div className="mt-2 flex flex-wrap items-center gap-1">
          {message.reactions.map((reaction) => {
            const mine = Boolean(currentUserId) && reaction.userIds.includes(currentUserId!);
            return (
              <button
                key={reaction.emoji}
                type="button"
                className={cn(
                  "rounded-full border px-2 py-0.5 text-xs",
                  mine ? "border-primary bg-primary/10" : "border-border bg-background text-muted-foreground",
                )}
                onClick={() => onToggleReaction(message, reaction.emoji, mine)}
              >
                {reaction.emoji} {reaction.userIds.length}
              </button>
            );
          })}
          <div className="relative">
            <button
              type="button"
              aria-haspopup="true"
              aria-expanded={pickerOpen}
              className="rounded-full border border-dashed border-border px-2 py-0.5 text-xs text-muted-foreground hover:bg-muted"
              onClick={() => setPickerOpen((open) => !open)}
            >
              + React
            </button>
            {pickerOpen ? (
              <div
                role="menu"
                aria-label="Add a reaction"
                className="absolute left-0 top-7 z-20 flex gap-1 rounded-lg border border-border bg-card p-1 text-base shadow-xl"
              >
                {QUICK_REACTIONS.map((emoji) => (
                  <button
                    key={emoji}
                    type="button"
                    role="menuitem"
                    className="rounded px-1 hover:bg-muted"
                    onClick={() => {
                      const existing = message.reactions.find((r) => r.emoji === emoji);
                      const mine = Boolean(currentUserId) && Boolean(existing?.userIds.includes(currentUserId!));
                      onToggleReaction(message, emoji, mine);
                      setPickerOpen(false);
                    }}
                  >
                    {emoji}
                  </button>
                ))}
              </div>
            ) : null}
          </div>
        </div>
      ) : null}

      {!isEditing ? (
        <div className="mt-3 flex flex-wrap items-center gap-2 text-xs">
          {!message.isDeleted ? (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="h-8 px-2 text-xs"
              onClick={() => onReply(parentMessageId)}
            >
              Reply
            </Button>
          ) : null}
          {canChange ? (
            <>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-8 px-2 text-xs"
                onClick={() => setIsEditing(true)}
              >
                Edit
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-8 px-2 text-xs text-red-600 hover:bg-red-50 dark:text-red-400 dark:hover:bg-red-950"
                onClick={() => onDelete(message)}
                disabled={isBusy}
              >
                Delete
              </Button>
            </>
          ) : null}
        </div>
      ) : null}
    </article>
  );
}

type MessageThreadProps = {
  isBusy: boolean;
  messages: ChatMessage[];
  onDelete: (message: ChatMessage) => void;
  onEdit: (message: ChatMessage, body: string) => void;
  onReply: (parentMessageId: string) => void;
  onToggleReaction: (message: ChatMessage, emoji: string, alreadyReacted: boolean) => void;
};

function MessageThread({ isBusy, messages, onDelete, onEdit, onReply, onToggleReaction }: MessageThreadProps) {
  const threads = useMemo(() => buildThreads(messages), [messages]);

  if (messages.length === 0) {
    return (
      <EmptyState
        title="No messages yet"
        description="Send the first update to start this channel thread."
      />
    );
  }

  return (
    <ol className="space-y-4" aria-label="Channel messages">
      {threads.map((message) => (
        <li key={message.id}>
          <MessageItem
            depth={0}
            message={message}
            isBusy={isBusy}
            onDelete={onDelete}
            onEdit={onEdit}
            onReply={onReply}
            onToggleReaction={onToggleReaction}
          />
          {message.replies.length > 0 ? (
            <ol className="mt-3 space-y-3 border-l border-border pl-4" aria-label="Replies">
              {message.replies.map((reply) => (
                <li key={reply.id}>
                  <MessageItem
                    depth={1}
                    message={reply}
                    isBusy={isBusy}
                    onDelete={onDelete}
                    onEdit={onEdit}
                    onReply={onReply}
                    onToggleReaction={onToggleReaction}
                  />
                </li>
              ))}
            </ol>
          ) : null}
        </li>
      ))}
    </ol>
  );
}

type MessageComposerProps = {
  disabled: boolean;
  isSending: boolean;
  replyTarget: ChatMessage | null;
  workspaceId: string | null;
  channelId: string | null;
  onCancelReply: () => void;
  onSend: (body: string, mentionUserIds: string[], files: File[]) => void;
};

function MessageComposer({
  disabled,
  isSending,
  replyTarget,
  workspaceId,
  channelId,
  onCancelReply,
  onSend,
}: MessageComposerProps) {
  const menuId = useId();
  const directory = useMemberDirectory();
  const currentUserId = useCurrentUserId();
  const { data: members } = useMembers();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [body, setBody] = useState("");
  const [mentionUserIds, setMentionUserIds] = useState<string[]>([]);
  const [mentionOpen, setMentionOpen] = useState(false);
  const [files, setFiles] = useState<File[]>([]);
  const broadcastTyping = useTypingBroadcast(workspaceId, "ChatChannel", channelId);
  const canSend = body.trim().length > 0 && !disabled && !isSending;
  const mentionableMembers = useMemo(
    () =>
      (members ?? [])
        .filter((member) => member.userId !== currentUserId)
        .map((member) => ({
          id: member.userId,
          name: directory.getLabel(member.userId),
          initials: directory.getInitials(member.userId),
          avatarUrl: directory.getAvatarUrl(member.userId),
        })),
    [members, currentUserId, directory],
  );

  function toggleMention(userId: string) {
    const member = mentionableMembers.find((item) => item.id === userId);

    setMentionUserIds((current) =>
      current.includes(userId) ? current.filter((id) => id !== userId) : [...current, userId],
    );

    if (member && !body.includes(`@${member.name}`)) {
      setBody((current) => `${current}${current.trim() ? " " : ""}@${member.name} `);
    }
  }

  function submitBody() {
    if (!canSend) {
      return;
    }

    onSend(body.trim(), mentionUserIds, files);
    setBody("");
    setMentionUserIds([]);
    setMentionOpen(false);
    setFiles([]);
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    submitBody();
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Escape" && replyTarget) {
      event.preventDefault();
      onCancelReply();
      return;
    }

    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      submitBody();
    }
  }

  function handleFilesChosen(event: ChangeEvent<HTMLInputElement>) {
    const chosen = Array.from(event.target.files ?? []);
    if (chosen.length > 0) {
      setFiles((current) => [...current, ...chosen]);
    }
  }

  const { isDraggingOver, dropZoneProps } = useFileDropZone((dropped) => {
    setFiles((current) => [...current, ...dropped]);
  }, disabled || isSending);

  return (
    <form
      className={`border-t bg-card p-4 transition-colors ${
        isDraggingOver ? "border-primary bg-primary/5" : "border-border"
      }`}
      onSubmit={handleSubmit}
      {...dropZoneProps}
    >
      {replyTarget ? (
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2 rounded-lg border border-border bg-background px-3 py-2 text-xs text-muted-foreground">
          <span>
            Replying to {directory.getLabel(replyTarget.authorUserId)}: “
            {replyTarget.isDeleted ? "message deleted" : replyTarget.body.slice(0, 80)}”
          </span>
          <Button type="button" variant="ghost" size="sm" className="h-8 px-2 text-xs" onClick={onCancelReply}>
            Cancel reply
          </Button>
        </div>
      ) : null}
      {disabled ? (
        <p className="mb-3 rounded-lg border border-border bg-muted px-3 py-2 text-sm font-medium text-muted-foreground">
          This channel is archived
        </p>
      ) : null}
      <div className="grid gap-2">
        <label htmlFor="chat-message-composer" className="text-sm font-medium">
          Message
        </label>
        <textarea
          id="chat-message-composer"
          value={body}
          rows={3}
          placeholder="Write a message…"
          className="min-h-24 rounded-lg border border-border bg-background px-3 py-2 text-sm shadow-sm outline-none transition placeholder:text-muted-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50 motion-reduce:transition-none"
          onChange={(event) => {
            setBody(event.target.value);
            broadcastTyping();
          }}
          onKeyDown={handleKeyDown}
          disabled={disabled || isSending}
        />
      </div>

      {files.length > 0 ? (
        <ul className="mt-2 flex flex-wrap gap-2" aria-label="Files to attach">
          {files.map((file, index) => (
            <li
              key={`${file.name}-${index}`}
              className="flex items-center gap-2 rounded-full bg-muted px-3 py-1 text-xs text-muted-foreground"
            >
              📎 {file.name}
              <button
                type="button"
                aria-label={`Remove ${file.name}`}
                className="text-muted-foreground hover:text-foreground"
                onClick={() => setFiles((current) => current.filter((_, i) => i !== index))}
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      <div className="relative mt-3 flex flex-wrap items-center gap-2">
        <input
          ref={fileInputRef}
          type="file"
          multiple
          className="sr-only"
          id="chat-message-attach"
          onChange={handleFilesChosen}
          disabled={disabled || isSending}
        />
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled || isSending}
          onClick={() => fileInputRef.current?.click()}
        >
          📎 Attach
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-haspopup="listbox"
          aria-expanded={mentionOpen}
          aria-controls={menuId}
          disabled={disabled || isSending}
          onClick={() => setMentionOpen((open) => !open)}
        >
          @ Mention
        </Button>
        {mentionUserIds.map((userId) => {
          const member = mentionableMembers.find((item) => item.id === userId);
          return (
            <span key={userId} className="rounded-full bg-muted px-2 py-1 text-xs text-muted-foreground">
              @{member?.name ?? userId}
            </span>
          );
        })}
        {mentionOpen ? (
          <div
            id={menuId}
            role="listbox"
            aria-label="Mention teammates"
            aria-multiselectable="true"
            className="absolute left-0 top-11 z-20 w-64 rounded-xl border border-border bg-card p-2 text-sm shadow-xl"
          >
            {mentionableMembers.length === 0 ? (
              <p className="px-2 py-2 text-xs text-muted-foreground">No other workspace members yet.</p>
            ) : (
              mentionableMembers.map((member) => {
                const selected = mentionUserIds.includes(member.id);
                return (
                  <button
                    key={member.id}
                    type="button"
                    role="option"
                    aria-selected={selected}
                    className="flex w-full items-center gap-2 rounded-lg px-2 py-2 text-left hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
                    onClick={() => toggleMention(member.id)}
                  >
                    <Avatar
                      avatarUrl={member.avatarUrl}
                      initials={member.initials}
                      className="grid size-7 place-items-center rounded-full bg-muted text-xs font-semibold"
                    />
                    <span className="flex-1">{member.name}</span>
                    <span className="text-xs text-muted-foreground">{selected ? "Selected" : "Add"}</span>
                  </button>
                );
              })
            )}
          </div>
        ) : null}
      </div>

      <div className="mt-3 flex items-center justify-between gap-3">
        <p className="text-xs text-muted-foreground">Press Enter to send, Shift+Enter for a new line.</p>
        <Button type="submit" size="sm" disabled={!canSend}>
          {isSending ? "Sending…" : "Send"}
        </Button>
      </div>
    </form>
  );
}

type ChannelSettingsProps = {
  channel: ChatChannel;
  isBusy: boolean;
  onAddMember: (userId: string) => void;
  onClose: () => void;
  onRemoveMember: (userId: string) => void;
  onRename: (input: { name: string; description: string | null }) => void;
};

/** Small inline settings panel: channel rename plus add/remove members. */
function ChannelSettings({
  channel,
  isBusy,
  onAddMember,
  onClose,
  onRemoveMember,
  onRename,
}: ChannelSettingsProps) {
  const directory = useMemberDirectory();
  const currentUserId = useCurrentUserId();
  const { data: workspaceMembers } = useMembers();
  const [name, setName] = useState(channel.name);
  const [description, setDescription] = useState(channel.description ?? "");
  const [memberToAdd, setMemberToAdd] = useState("");
  const isDirect = channel.channelType === "Dm" || channel.channelType === "GroupDm";
  const addableMembers = (workspaceMembers ?? []).filter(
    (member) => !channel.memberUserIds.includes(member.userId),
  );

  function handleRename(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!name.trim()) {
      return;
    }

    onRename({ name: name.trim(), description: description.trim() || null });
  }

  return (
    <section
      aria-label={`Settings for ${channelDisplayName(channel, currentUserId, directory)}`}
      className="border-b border-border bg-background p-4"
    >
      <div className="flex items-start justify-between gap-3">
        <h3 className="text-sm font-semibold">Channel settings</h3>
        <Button type="button" variant="ghost" size="sm" onClick={onClose}>
          Done
        </Button>
      </div>

      {isDirect ? (
        <p className="mt-3 text-xs text-muted-foreground">
          Direct messages cannot be renamed and are only visible to their participants.
        </p>
      ) : (
        <form className="mt-3 grid gap-3 md:max-w-lg" onSubmit={handleRename}>
          <Input
            id="channel-settings-name"
            label="Channel name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            disabled={isBusy}
            required
          />
          <div className="grid gap-2">
            <label htmlFor="channel-settings-description" className="text-sm font-medium">
              Description
            </label>
            <textarea
              id="channel-settings-description"
              value={description}
              className="min-h-16 rounded-lg border border-border bg-background px-3 py-2 text-sm shadow-sm outline-none transition placeholder:text-muted-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none"
              onChange={(event) => setDescription(event.target.value)}
              disabled={isBusy}
            />
          </div>
          <Button type="submit" size="sm" className="w-fit" disabled={!name.trim() || isBusy}>
            Save channel
          </Button>
        </form>
      )}

      <div className="mt-5">
        <h4 className="text-sm font-semibold">Members</h4>
        <ul className="mt-2 space-y-2" aria-label="Channel members">
          {channel.memberUserIds.map((userId) => (
            <li
              key={userId}
              className="flex items-center justify-between gap-3 rounded-lg border border-border bg-card px-3 py-2 text-sm"
            >
              <span>{directory.getLabel(userId)}</span>
              {!isDirect ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-8 px-2 text-xs text-red-600 hover:bg-red-50 dark:text-red-400 dark:hover:bg-red-950"
                  disabled={isBusy}
                  onClick={() => onRemoveMember(userId)}
                >
                  Remove
                </Button>
              ) : null}
            </li>
          ))}
          {channel.memberUserIds.length === 0 ? (
            <li className="text-sm text-muted-foreground">No members yet.</li>
          ) : null}
        </ul>

        {!isDirect ? (
          <div className="mt-3 flex flex-wrap items-end gap-2">
            <div className="grid gap-2">
              <label htmlFor="channel-add-member" className="text-sm font-medium">
                Add member
              </label>
              <select
                id="channel-add-member"
                value={memberToAdd}
                className="h-10 rounded-lg border border-border bg-background px-3 text-sm shadow-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                onChange={(event) => setMemberToAdd(event.target.value)}
                disabled={isBusy || addableMembers.length === 0}
              >
                <option value="">
                  {addableMembers.length === 0 ? "Everyone is already a member" : "Select a teammate…"}
                </option>
                {addableMembers.map((member) => (
                  <option key={member.userId} value={member.userId}>
                    {member.displayName || member.email || member.userId}
                  </option>
                ))}
              </select>
            </div>
            <Button
              type="button"
              size="sm"
              variant="outline"
              disabled={!memberToAdd || isBusy}
              onClick={() => {
                onAddMember(memberToAdd);
                setMemberToAdd("");
              }}
            >
              Add
            </Button>
          </div>
        ) : null}
      </div>
    </section>
  );
}

export function ChatPageClient() {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  // A search hit or other deep link can land here with ?channel= — that becomes the initial selection
  // just like a manual click would, and stays subject to the same "still exists" membership check below.
  const searchParams = useSearchParams();
  const [manualSelectedChannelId, setManualSelectedChannelId] = useState<string | null>(
    searchParams.get("channel"),
  );
  const [settingsChannelId, setSettingsChannelId] = useState<string | null>(null);
  const [replySelection, setReplySelection] = useState<{ channelId: string; messageId: string } | null>(null);
  const directory = useMemberDirectory();
  const currentUserId = useCurrentUserId();

  const channelsQuery = useQuery({
    queryKey: chatKeys.channels(workspaceId),
    queryFn: listChannels,
  });
  const channels = useMemo(() => channelsQuery.data ?? [], [channelsQuery.data]);

  const defaultChannelId = channels.find((channel) => !channel.isArchived)?.id ?? channels[0]?.id ?? null;
  const selectedChannelId =
    manualSelectedChannelId && channels.some((channel) => channel.id === manualSelectedChannelId)
      ? manualSelectedChannelId
      : defaultChannelId;
  const selectedSummary = channels.find((channel) => channel.id === selectedChannelId) ?? null;
  useRecordRecentView("chatchannel", selectedChannelId);

  const channelQuery = useQuery({
    queryKey: chatKeys.channel(workspaceId, selectedChannelId ?? "none"),
    queryFn: () => {
      if (!selectedChannelId) {
        throw new Error("Select a channel first.");
      }

      return getChannel(selectedChannelId);
    },
    enabled: Boolean(selectedChannelId),
  });

  const messagesQuery = useQuery({
    queryKey: chatKeys.messages(workspaceId, selectedChannelId ?? "none"),
    queryFn: () => {
      if (!selectedChannelId) {
        throw new Error("Select a channel first.");
      }

      return listMessages(selectedChannelId);
    },
    enabled: Boolean(selectedChannelId),
  });

  const messages = useMemo(() => messagesQuery.data ?? [], [messagesQuery.data]);
  const replyTarget = useMemo(
    () =>
      replySelection?.channelId === selectedChannelId
        ? messages.find((message) => message.id === replySelection.messageId) ?? null
        : null,
    [messages, replySelection, selectedChannelId],
  );

  const invalidateMessages = (channelId: string) => {
    void queryClient.invalidateQueries({ queryKey: chatKeys.messages(workspaceId, channelId) });
  };

  // Unread badges in the sidebar: mark the channel read once its messages have loaded, so the badge
  // clears the moment the user actually opens the thread rather than requiring a manual action.
  useEffect(() => {
    if (!selectedChannelId || messages.length === 0) {
      return;
    }

    const summary = channels.find((channel) => channel.id === selectedChannelId);
    if (!summary || summary.unreadCount === 0) {
      return;
    }

    const lastMessageId = messages[messages.length - 1]?.id ?? null;
    void markChannelRead(selectedChannelId, lastMessageId).then(() =>
      queryClient.invalidateQueries({ queryKey: chatKeys.channels(workspaceId) }),
    );
  }, [selectedChannelId, messages, channels, workspaceId, queryClient]);

  const createChannelMutation = useMutation({
    mutationFn: createChannel,
    onSuccess: (channel) => {
      setManualSelectedChannelId(channel.id);
      void queryClient.invalidateQueries({ queryKey: chatKeys.channels(workspaceId) });
    },
  });

  const createLinkedChannelMutation = useMutation({
    mutationFn: createLinkedChannel,
    onSuccess: (channel) => {
      setManualSelectedChannelId(channel.id);
      void queryClient.invalidateQueries({ queryKey: chatKeys.channels(workspaceId) });
    },
  });

  const createDirectMessageMutation = useMutation({
    mutationFn: createDirectMessage,
    onSuccess: (channel) => {
      setManualSelectedChannelId(channel.id);
      void queryClient.invalidateQueries({ queryKey: chatKeys.channels(workspaceId) });
    },
  });

  const archiveChannelMutation = useMutation({
    mutationFn: archiveChannel,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: chatKeys.channels(workspaceId) });
    },
  });

  // `chatKeys.channels(workspaceId)` is a prefix of `chatKeys.channel(...)`, so one invalidation
  // refreshes both the sidebar list and the open channel.
  const invalidateChannels = () =>
    queryClient.invalidateQueries({ queryKey: chatKeys.channels(workspaceId) });

  const updateChannelMutation = useMutation({
    mutationFn: ({
      channelId,
      name,
      description,
    }: {
      channelId: string;
      name: string;
      description: string | null;
    }) => updateChannel(channelId, { name, description }),
    onSuccess: () => invalidateChannels(),
  });

  const addMemberMutation = useMutation({
    mutationFn: ({ channelId, userId }: { channelId: string; userId: string }) =>
      addMember(channelId, userId),
    onSuccess: () => invalidateChannels(),
  });

  const removeMemberMutation = useMutation({
    mutationFn: ({ channelId, userId }: { channelId: string; userId: string }) =>
      removeMember(channelId, userId),
    onSuccess: () => invalidateChannels(),
  });

  const postMessageMutation = useMutation({
    mutationFn: async ({
      channelId,
      body,
      parentMessageId,
      mentionUserIds,
      files,
    }: {
      channelId: string;
      body: string;
      parentMessageId: string | null;
      mentionUserIds: string[];
      files: File[];
    }) => {
      const message = await postMessage(channelId, { body, parentMessageId, mentionUserIds });
      for (const file of files) {
        await uploadAttachment(message.id, file);
      }

      return message;
    },
    onSuccess: (_message, variables) => {
      setReplySelection(null);
      invalidateMessages(variables.channelId);
    },
  });

  const editMessageMutation = useMutation({
    mutationFn: ({ messageId, body }: { messageId: string; channelId: string; body: string }) =>
      editMessage(messageId, { body }),
    onSuccess: (_message, variables) => invalidateMessages(variables.channelId),
  });

  const deleteMessageMutation = useMutation({
    mutationFn: ({ messageId }: { messageId: string; channelId: string }) => deleteMessage(messageId),
    onSuccess: (_message, variables) => invalidateMessages(variables.channelId),
  });

  const reactionMutation = useMutation({
    mutationFn: ({
      messageId,
      emoji,
      remove,
    }: {
      messageId: string;
      channelId: string;
      emoji: string;
      remove: boolean;
    }) => (remove ? removeReaction(messageId, emoji) : addReaction(messageId, emoji)),
    onSuccess: (_message, variables) => invalidateMessages(variables.channelId),
  });

  const mutationError =
    createChannelMutation.error ??
    createLinkedChannelMutation.error ??
    createDirectMessageMutation.error ??
    archiveChannelMutation.error ??
    updateChannelMutation.error ??
    addMemberMutation.error ??
    removeMemberMutation.error ??
    postMessageMutation.error ??
    editMessageMutation.error ??
    deleteMessageMutation.error ??
    reactionMutation.error;
  const isChannelMutationPending =
    updateChannelMutation.isPending ||
    addMemberMutation.isPending ||
    removeMemberMutation.isPending;
  const selectedChannel = channelQuery.data ?? null;
  const channelForHeader = selectedChannel ?? selectedSummary;
  const isMessageMutationPending =
    postMessageMutation.isPending || editMessageMutation.isPending || deleteMessageMutation.isPending;
  const isCreatingChannel =
    createChannelMutation.isPending || createLinkedChannelMutation.isPending || createDirectMessageMutation.isPending;

  function handleSendMessage(body: string, mentionUserIds: string[], files: File[]) {
    if (!selectedChannelId) {
      return;
    }

    postMessageMutation.mutate({
      channelId: selectedChannelId,
      body,
      parentMessageId: replyTarget?.id ?? null,
      mentionUserIds,
      files,
    });
  }

  function handleEditMessage(message: ChatMessage, body: string) {
    editMessageMutation.mutate({
      messageId: message.id,
      channelId: message.channelId,
      body,
    });
  }

  function handleDeleteMessage(message: ChatMessage) {
    deleteMessageMutation.mutate({
      messageId: message.id,
      channelId: message.channelId,
    });
  }

  function handleToggleReaction(message: ChatMessage, emoji: string, alreadyReacted: boolean) {
    reactionMutation.mutate({ messageId: message.id, channelId: message.channelId, emoji, remove: alreadyReacted });
  }

  const headerBadge = channelForHeader ? channelKindBadge(channelForHeader.channelType) : null;
  const headerDisplayName = channelForHeader
    ? channelDisplayName(channelForHeader, currentUserId, directory)
    : null;

  return (
    <section aria-labelledby="chat-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Workspace conversations</p>
          <h1 id="chat-title" className="mt-2 text-3xl font-semibold tracking-tight">
            Chat
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            Channels, private channels, direct messages, and resource-linked channels — with threaded
            replies, reactions, attachments, mentions, and read history.
          </p>
        </div>
        <span className="rounded-full bg-primary/10 px-3 py-1 text-xs font-semibold text-primary">
          {channels.length} channels
        </span>
      </div>

      {mutationError ? (
        <p
          role="alert"
          className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm font-medium text-red-700 dark:border-red-900 dark:bg-red-950 dark:text-red-300"
        >
          {mutationError.message}
        </p>
      ) : null}

      <div className="grid min-h-[calc(100vh-14rem)] gap-4 xl:grid-cols-[21rem_minmax(0,1fr)]">
        <aside
          aria-labelledby="chat-channel-list-title"
          className="space-y-4 rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm"
        >
          <NewChannelForm
            isCreating={isCreatingChannel}
            onCreate={(input) => createChannelMutation.mutate(input)}
            onCreateLinked={(input) => createLinkedChannelMutation.mutate(input)}
            onCreateDirectMessage={(input) => createDirectMessageMutation.mutate(input)}
          />
          <div className="space-y-3">
            <div className="flex items-center justify-between gap-3">
              <h2 id="chat-channel-list-title" className="text-sm font-semibold">
                Channels
              </h2>
              <span className="text-xs text-muted-foreground">{channels.length}</span>
            </div>
            <ChannelList
              channels={channels}
              isLoading={channelsQuery.isLoading}
              isError={channelsQuery.isError}
              error={channelsQuery.error}
              onRetry={() => void channelsQuery.refetch()}
              selectedChannelId={selectedChannelId}
              currentUserId={currentUserId}
              directory={directory}
              onSelect={(channelId) => {
                setManualSelectedChannelId(channelId);
                setReplySelection(null);
              }}
            />
          </div>
        </aside>

        <section
          aria-labelledby="selected-channel-title"
          className="flex min-h-[40rem] flex-col overflow-hidden rounded-[var(--radius)] border border-border bg-card shadow-sm"
        >
          {!selectedChannelId ? (
            <div className="p-4">
              <EmptyState
                title="Select a channel"
                description="Choose a workspace channel or create a new one to view messages."
              />
            </div>
          ) : (
            <>
              <header className="flex flex-col gap-3 border-b border-border bg-muted/60 p-4 md:flex-row md:items-start md:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 id="selected-channel-title" className="text-lg font-semibold">
                      {headerDisplayName ?? "Loading channel…"}
                    </h2>
                    {headerBadge ? (
                      <span className="rounded-full bg-card px-2 py-0.5 text-xs font-medium text-muted-foreground">
                        {headerBadge.icon} {headerBadge.label}
                      </span>
                    ) : null}
                    {channelForHeader?.isArchived ? (
                      <span className="rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                        Archived
                      </span>
                    ) : null}
                  </div>
                  <p className="mt-2 max-w-2xl text-sm leading-6 text-muted-foreground">
                    {channelForHeader?.description ?? "No description provided."}
                  </p>
                  {selectedChannel ? (
                    <p className="mt-2 text-xs text-muted-foreground">
                      {selectedChannel.memberUserIds.length} members · Created{" "}
                      <time dateTime={selectedChannel.createdAtUtc}>
                        {formatTimestamp(selectedChannel.createdAtUtc)}
                      </time>
                    </p>
                  ) : null}
                </div>
                {selectedChannel && !selectedChannel.isArchived ? (
                  <div className="flex flex-wrap items-center gap-2">
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      aria-expanded={settingsChannelId === selectedChannel.id}
                      onClick={() =>
                        setSettingsChannelId((current) =>
                          current === selectedChannel.id ? null : selectedChannel.id,
                        )
                      }
                    >
                      Settings
                    </Button>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => archiveChannelMutation.mutate(selectedChannel.id)}
                      disabled={archiveChannelMutation.isPending}
                    >
                      Archive
                    </Button>
                  </div>
                ) : null}
              </header>

              {selectedChannel && settingsChannelId === selectedChannel.id ? (
                <ChannelSettings
                  channel={selectedChannel}
                  isBusy={isChannelMutationPending}
                  onAddMember={(userId) =>
                    addMemberMutation.mutate({ channelId: selectedChannel.id, userId })
                  }
                  onClose={() => setSettingsChannelId(null)}
                  onRemoveMember={(userId) =>
                    removeMemberMutation.mutate({ channelId: selectedChannel.id, userId })
                  }
                  onRename={(input) =>
                    updateChannelMutation.mutate({ channelId: selectedChannel.id, ...input })
                  }
                />
              ) : null}

              <div className="min-h-0 flex-1 overflow-y-auto bg-background p-4">
                {channelQuery.isLoading || messagesQuery.isLoading ? (
                  <div className="rounded-xl border border-border bg-card p-4 text-sm text-muted-foreground">
                    Loading messages…
                  </div>
                ) : (
                  <MessageThread
                    messages={messages}
                    isBusy={isMessageMutationPending}
                    onDelete={handleDeleteMessage}
                    onEdit={handleEditMessage}
                    onToggleReaction={handleToggleReaction}
                    onReply={(messageId) => {
                      if (!selectedChannelId) {
                        return;
                      }

                      setReplySelection({ channelId: selectedChannelId, messageId });
                    }}
                  />
                )}
              </div>

              <TypingIndicator resourceType="ChatChannel" resourceId={selectedChannelId} />
              <MessageComposer
                disabled={Boolean(selectedChannel?.isArchived)}
                isSending={postMessageMutation.isPending}
                replyTarget={replyTarget}
                workspaceId={workspaceId || null}
                channelId={selectedChannelId}
                onCancelReply={() => setReplySelection(null)}
                onSend={handleSendMessage}
              />
            </>
          )}
        </section>
      </div>
    </section>
  );
}
