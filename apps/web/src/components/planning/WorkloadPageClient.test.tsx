import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { WorkloadPageClient } from "./WorkloadPageClient";
import type { DrillDownTask, WorkloadRow } from "@/lib/planning/types";

const UNASSIGNED_USER_ID = "00000000-0000-0000-0000-000000000000";

const getWorkloadMock = vi.fn<() => Promise<WorkloadRow[]>>();
const getAssigneeDrillDownMock = vi.fn<(userId: string) => Promise<DrillDownTask[]>>();

vi.mock("@/lib/planning/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/planning/client")>();
  return {
    ...actual,
    getWorkload: () => getWorkloadMock(),
    getAssigneeDrillDown: (userId: string) => getAssigneeDrillDownMock(userId),
  };
});

vi.mock("@/lib/members", () => ({
  useMemberDirectory: () => ({
    getLabel: (userId: string) => `Member ${userId}`,
    getInitials: (userId: string) => userId.slice(0, 2).toUpperCase(),
    getAvatarUrl: () => null,
  }),
}));

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <WorkloadPageClient />
    </QueryClientProvider>,
  );
}

describe("WorkloadPageClient", () => {
  it("shows an Unassigned row when unassigned tasks exist, and links it to a filtered task list", async () => {
    const user = userEvent.setup();
    getWorkloadMock.mockResolvedValue([
      { userId: "user-1", capacityHours: 40, scheduledHours: 10, loggedHours: 5, isOverAllocated: false },
      { userId: UNASSIGNED_USER_ID, capacityHours: 0, scheduledHours: 6, loggedHours: 0, isOverAllocated: false },
    ]);
    getAssigneeDrillDownMock.mockResolvedValue([
      { taskId: "task-9", title: "Orphaned task", statusName: "To Do", isCompleted: false },
    ]);

    renderPage();

    const unassignedRowButton = await screen.findByRole("button", { name: /view tasks for unassigned/i });
    expect(unassignedRowButton).toBeInTheDocument();
    expect(screen.getByText("Nobody owns these tasks")).toBeInTheDocument();

    await user.click(unassignedRowButton);

    expect(getAssigneeDrillDownMock).toHaveBeenCalledWith(UNASSIGNED_USER_ID);
    await waitFor(() => expect(screen.getByText("Orphaned task")).toBeInTheDocument());
  });

  it("does not render an Unassigned row when every task is assigned", async () => {
    getWorkloadMock.mockResolvedValue([
      { userId: "user-1", capacityHours: 40, scheduledHours: 10, loggedHours: 5, isOverAllocated: false },
    ]);

    renderPage();

    await screen.findByText("Member user-1");
    expect(screen.queryByText("Unassigned")).not.toBeInTheDocument();
  });

  it("links a teammate row to their own filtered task list", async () => {
    const user = userEvent.setup();
    getWorkloadMock.mockResolvedValue([
      { userId: "user-1", capacityHours: 40, scheduledHours: 10, loggedHours: 5, isOverAllocated: false },
    ]);
    getAssigneeDrillDownMock.mockResolvedValue([]);

    renderPage();

    await user.click(await screen.findByRole("button", { name: /view tasks for member user-1/i }));
    expect(getAssigneeDrillDownMock).toHaveBeenCalledWith("user-1");
  });

  it("does not leak the raw API path as a user-facing stat", async () => {
    getWorkloadMock.mockResolvedValue([]);
    renderPage();

    await screen.findByText(/no scheduled work yet/i);
    expect(screen.queryByText("/api/v1/views/workload")).not.toBeInTheDocument();
    expect(screen.queryByText(/data source/i)).not.toBeInTheDocument();
    expect(screen.getByText(/teammates shown/i)).toBeInTheDocument();
  });
});
