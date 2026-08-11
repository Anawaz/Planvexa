import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { NotificationBell, notificationHref } from "./NotificationBell";
import type { Notification } from "@/lib/collab/types";

const listNotificationsMock = vi.fn<() => Promise<Notification[]>>();
const unreadCountMock = vi.fn<() => Promise<number>>();
const markReadMock = vi.fn<(id: string) => Promise<void>>();
const markAllReadMock = vi.fn<() => Promise<void>>();

vi.mock("@/lib/collab/client", () => ({
  listNotifications: () => listNotificationsMock(),
  unreadCount: () => unreadCountMock(),
  markRead: (id: string) => markReadMock(id),
  markAllRead: () => markAllReadMock(),
}));

vi.mock("@/lib/members", () => ({
  useMemberDirectory: () => ({ getLabel: (userId: string) => userId }),
}));

function taskNotification(overrides: Partial<Notification> = {}): Notification {
  return {
    id: "notif-1",
    eventType: "assignment",
    entityType: "Task",
    entityId: "task-1",
    workspaceId: "ws-1",
    payload: { taskTitle: "Ship the feature" },
    createdAtUtc: new Date().toISOString(),
    readAtUtc: null,
    ...overrides,
  };
}

function renderBell() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <NotificationBell />
    </QueryClientProvider>,
  );
}

describe("notificationHref", () => {
  it("links a Task notification to its My Work deep link", () => {
    expect(notificationHref(taskNotification())).toBe("/app/my-work?task=task-1");
  });

  it("links a ChatMessage notification to its channel using the payload channelId", () => {
    expect(
      notificationHref(
        taskNotification({ entityType: "ChatMessage", entityId: "msg-1", payload: { channelId: "chan-1" } }),
      ),
    ).toBe("/app/chat?channel=chan-1");
  });

  it("has nowhere to send a ChatMessage notification missing a channelId", () => {
    expect(notificationHref(taskNotification({ entityType: "ChatMessage", payload: {} }))).toBeNull();
  });

  it("links a Workspace notification (e.g. invitation accepted) to Members", () => {
    expect(notificationHref(taskNotification({ entityType: "Workspace" }))).toBe("/app/members");
  });

  it("has nowhere to send an unrecognized entity type", () => {
    expect(notificationHref(taskNotification({ entityType: "SomethingNew" }))).toBeNull();
  });
});

describe("NotificationBell", () => {
  beforeEach(() => {
    listNotificationsMock.mockReset();
    unreadCountMock.mockReset();
    markReadMock.mockReset();
    markAllReadMock.mockReset();
    unreadCountMock.mockResolvedValue(1);
    markReadMock.mockResolvedValue(undefined);
  });

  it("renders a deep link for a notification with a resolvable resource and marks it read on click", async () => {
    listNotificationsMock.mockResolvedValue([taskNotification()]);
    renderBell();

    fireEvent.click(screen.getByRole("button", { name: /Notifications/ }));

    const link = await waitFor(() => screen.getByRole("link", { name: /Ship the feature/ }));
    expect(link).toHaveAttribute("href", "/app/my-work?task=task-1");

    fireEvent.click(link);
    await waitFor(() => expect(markReadMock).toHaveBeenCalledWith("notif-1"));
  });

  it("falls back to a plain mark-as-read button when the notification has no deep link", async () => {
    listNotificationsMock.mockResolvedValue([taskNotification({ entityType: "Unknown" })]);
    renderBell();

    fireEvent.click(screen.getByRole("button", { name: /Notifications/ }));

    const row = await waitFor(() => screen.getByRole("button", { name: /Ship the feature/ }));
    expect(screen.queryByRole("link", { name: /Ship the feature/ })).not.toBeInTheDocument();

    fireEvent.click(row);
    await waitFor(() => expect(markReadMock).toHaveBeenCalledWith("notif-1"));
  });
});
