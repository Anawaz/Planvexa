import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ChatPageClient } from "./ChatPageClient";
import type { ChatChannel, ChatChannelSummary, ChatMessage } from "@/lib/chat/types";

const listChannelsMock = vi.fn<() => Promise<ChatChannelSummary[]>>();
const getChannelMock = vi.fn<() => Promise<ChatChannel>>();
const listMessagesMock = vi.fn<() => Promise<ChatMessage[]>>();
const postMessageMock = vi.fn<() => Promise<ChatMessage>>();
const uploadAttachmentMock = vi.fn<(messageId: string, file: File) => Promise<unknown>>();

vi.mock("@/lib/chat/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/chat/client")>();
  return {
    ...actual,
    listChannels: () => listChannelsMock(),
    getChannel: () => getChannelMock(),
    listMessages: () => listMessagesMock(),
    postMessage: () => postMessageMock(),
    uploadAttachment: (messageId: string, file: File) => uploadAttachmentMock(messageId, file),
  };
});

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("@/lib/members", () => ({
  useMemberDirectory: () => ({
    getLabel: (userId: string) => userId,
    getInitials: (userId: string) => userId.slice(0, 2).toUpperCase(),
    getAvatarUrl: () => null,
  }),
  useMembers: () => ({ data: [] }),
  useCurrentUserId: () => "user-1",
}));

vi.mock("@/lib/recent/useRecordRecentView", () => ({
  useRecordRecentView: () => {},
}));

vi.mock("@/lib/realtime/useRealtime", () => ({
  useTypingBroadcast: () => () => {},
  useTypingUsers: () => [],
}));

vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(),
}));

function channel(overrides: Partial<ChatChannelSummary> = {}): ChatChannelSummary {
  return {
    id: "channel-1",
    channelType: "Workspace",
    name: "general",
    isPrivate: false,
    isArchived: false,
    createdAtUtc: new Date().toISOString(),
    memberUserIds: ["user-1"],
    unreadCount: 0,
    ...overrides,
  };
}

function renderChat() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ChatPageClient />
    </QueryClientProvider>,
  );
}

describe("ChatPageClient message composer file drop", () => {
  beforeEach(() => {
    listChannelsMock.mockReset();
    getChannelMock.mockReset();
    listMessagesMock.mockReset();
    postMessageMock.mockReset();
    uploadAttachmentMock.mockReset();

    const summary = channel();
    listChannelsMock.mockResolvedValue([summary]);
    getChannelMock.mockResolvedValue({
      ...summary,
      createdByUserId: "user-1",
    });
    listMessagesMock.mockResolvedValue([]);
  });

  it("adds a file dropped onto the composer to the pending attachment list, same as the file input", async () => {
    renderChat();

    const textarea = await screen.findByLabelText("Message");
    const form = textarea.closest("form")!;
    const file = new File(["hello"], "hello.txt", { type: "text/plain" });

    fireEvent.drop(form, { dataTransfer: { types: ["Files"], files: [file] } });

    expect(await screen.findByText(/hello\.txt/)).toBeInTheDocument();
  });

  it("uploads a dropped file as a message attachment on send, same as a chosen file", async () => {
    postMessageMock.mockResolvedValue({
      id: "message-1",
      channelId: "channel-1",
      authorUserId: "user-1",
      body: "Here you go",
      isDeleted: false,
      createdAtUtc: new Date().toISOString(),
      mentionUserIds: [],
      reactions: [],
      attachments: [],
    });
    uploadAttachmentMock.mockResolvedValue(undefined);
    renderChat();

    const textarea = await screen.findByLabelText("Message");
    const form = textarea.closest("form")!;
    const file = new File(["hello"], "hello.txt", { type: "text/plain" });
    fireEvent.drop(form, { dataTransfer: { types: ["Files"], files: [file] } });
    await screen.findByText(/hello\.txt/);

    fireEvent.change(textarea, { target: { value: "Here you go" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    await waitFor(() => expect(uploadAttachmentMock).toHaveBeenCalledWith("message-1", file));
  });

  it("shows a drag-over highlight while a file is dragged over the composer", async () => {
    renderChat();

    const textarea = await screen.findByLabelText("Message");
    const form = textarea.closest("form")!;

    fireEvent.dragEnter(form, { dataTransfer: { types: ["Files"], files: [] } });
    expect(form.className).toContain("border-primary");

    fireEvent.dragLeave(form, { dataTransfer: { types: ["Files"], files: [] } });
    expect(form.className).not.toContain("border-primary");
  });
});

describe("ChatPageClient channel list loading/error/empty states", () => {
  beforeEach(() => {
    listChannelsMock.mockReset();
    getChannelMock.mockReset();
    listMessagesMock.mockReset();
  });

  it("shows a genuine error state (not 'No channels yet') when the channels query rejects", async () => {
    listChannelsMock.mockRejectedValue(new Error("boom"));
    renderChat();

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
    expect(screen.queryByText("No channels yet")).not.toBeInTheDocument();
  });

  it("shows 'No channels yet' (not an error) when the channels query resolves with none", async () => {
    listChannelsMock.mockResolvedValue([]);
    renderChat();

    await waitFor(() => expect(screen.getByText("No channels yet")).toBeInTheDocument());
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
