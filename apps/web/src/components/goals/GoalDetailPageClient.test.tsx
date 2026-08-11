import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { GoalDetailPageClient } from "./GoalDetailPageClient";
import type { GoalDetail } from "@/lib/goals/types";

const getGoalMock = vi.fn<() => Promise<GoalDetail>>();
const listGoalCommentsMock = vi.fn();

vi.mock("@/lib/goals/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/goals/client")>();
  return {
    ...actual,
    getGoal: () => getGoalMock(),
    listGoalComments: () => listGoalCommentsMock(),
  };
});

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <GoalDetailPageClient goalId="goal-1" />
    </QueryClientProvider>,
  );
}

describe("GoalDetailPageClient loading/error/not-found states", () => {
  beforeEach(() => {
    getGoalMock.mockReset();
    listGoalCommentsMock.mockReset().mockResolvedValue([]);
  });

  it("shows a genuine error state (not 'Goal not found') when the goal query rejects", async () => {
    getGoalMock.mockRejectedValue(new Error("boom"));
    renderPage();

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
    expect(screen.queryByText("Goal not found.")).not.toBeInTheDocument();
  });

  it("shows 'Goal not found' (not an error) when the query resolves with no goal", async () => {
    // The API contract always returns a goal for a 200; this simulates the defensive branch for a
    // response shaped without one (as opposed to a rejected/errored query).
    getGoalMock.mockResolvedValue({ goal: undefined, linkedTasks: [], keyResults: [] } as unknown as GoalDetail);
    renderPage();

    await waitFor(() => expect(screen.getByText("Goal not found.")).toBeInTheDocument());
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
