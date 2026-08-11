import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { GoalListPageClient } from "./GoalListPageClient";
import type { Goal } from "@/lib/goals/types";

const listGoalsMock = vi.fn<() => Promise<Goal[]>>();

vi.mock("@/lib/goals/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/goals/client")>();
  return { ...actual, listGoals: () => listGoalsMock() };
});

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <GoalListPageClient />
    </QueryClientProvider>,
  );
}

describe("GoalListPageClient loading/error/empty states", () => {
  beforeEach(() => {
    listGoalsMock.mockReset();
  });

  it("shows a genuine error state (not the empty state) when the goals query rejects", async () => {
    listGoalsMock.mockRejectedValue(new Error("boom"));
    renderPage();

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
    expect(screen.queryByText("No goals yet")).not.toBeInTheDocument();
  });

  it("shows the empty state (not an error) when the goals query resolves with no goals", async () => {
    listGoalsMock.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(screen.getByText("No goals yet")).toBeInTheDocument());
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
