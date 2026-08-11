import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OfflineIndicator } from "./OfflineIndicator";
import type { ConflictWarning, OutboxItem } from "@/lib/offline/db";

const dismissConflictMock = vi.fn();
let mockIsOnline = true;
let mockConflicts: ConflictWarning[] = [];
let mockItems: OutboxItem[] = [];

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("@/lib/offline/connectivity", () => ({
  useOnlineStatus: () => mockIsOnline,
}));

vi.mock("@/lib/offline/useOutboxStatus", () => ({
  useOutboxStatus: () => ({
    pendingCount: mockItems.filter((item) => item.status !== "error").length,
    errorCount: mockItems.filter((item) => item.status === "error").length,
    items: mockItems,
    conflicts: mockConflicts,
    dismissConflict: dismissConflictMock,
  }),
}));

const outboxUpdateMock = vi.fn();
const outboxRemoveMock = vi.fn();
vi.mock("@/lib/offline/db", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/offline/db")>();
  return {
    ...actual,
    outboxUpdate: (id: string, patch: Partial<OutboxItem>) => outboxUpdateMock(id, patch),
    outboxRemove: (id: string) => outboxRemoveMock(id),
  };
});

describe("OfflineIndicator", () => {
  beforeEach(() => {
    mockIsOnline = true;
    mockConflicts = [];
    mockItems = [];
    dismissConflictMock.mockClear();
    outboxUpdateMock.mockClear();
    outboxRemoveMock.mockClear();
  });

  it("renders nothing when online with no pending, error, or conflict state", () => {
    const { container } = render(<OfflineIndicator />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders a conflict and dismisses it via dismissConflict", async () => {
    mockConflicts = [
      {
        id: "conflict-1",
        workspaceId: "ws-1",
        taskId: "task-1",
        message: "This task was changed by someone else while you were offline (title).",
        fields: ["title"],
        createdAtUtc: new Date().toISOString(),
      },
    ];

    const user = userEvent.setup();
    render(<OfflineIndicator />);

    expect(screen.getByText(/changed by someone else while you were offline \(title\)/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Dismiss" }));
    expect(dismissConflictMock).toHaveBeenCalledWith("conflict-1");
  });

  it("retries a failed outbox item", async () => {
    mockItems = [
      {
        id: "outbox-1",
        workspaceId: "ws-1",
        type: "task.update",
        payload: {},
        createdAtUtc: new Date().toISOString(),
        status: "error",
        error: "Network error",
      },
    ];

    const user = userEvent.setup();
    render(<OfflineIndicator />);

    expect(screen.getByText(/task.update failed to sync: Network error/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Retry" }));
    expect(outboxUpdateMock).toHaveBeenCalledWith("outbox-1", { status: "pending", error: undefined });
  });

  it("discards a failed outbox item", async () => {
    mockItems = [
      {
        id: "outbox-2",
        workspaceId: "ws-1",
        type: "comment.create",
        payload: {},
        createdAtUtc: new Date().toISOString(),
        status: "error",
        error: "Server rejected the request",
      },
    ];

    const user = userEvent.setup();
    render(<OfflineIndicator />);

    await user.click(screen.getByRole("button", { name: "Discard" }));
    expect(outboxRemoveMock).toHaveBeenCalledWith("outbox-2");
  });
});
