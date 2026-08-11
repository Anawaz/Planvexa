import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { WhiteboardsListPageClient } from "./WhiteboardsListPageClient";
import type { SearchResult } from "@/lib/search/client";
import type { Whiteboard, WhiteboardTemplate } from "@/lib/collab/types";

const listWhiteboardsMock = vi.fn<() => Promise<Whiteboard[]>>();
const listWhiteboardTemplatesMock = vi.fn<() => Promise<WhiteboardTemplate[]>>();
const createWhiteboardMock = vi.fn<(input: Record<string, unknown>) => Promise<Whiteboard>>();
const searchMock = vi.fn<(term: string) => Promise<SearchResult[]>>();

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("@/lib/collab/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/collab/client")>();
  return {
    ...actual,
    listWhiteboards: () => listWhiteboardsMock(),
    listWhiteboardTemplates: () => listWhiteboardTemplatesMock(),
    createWhiteboard: (input: Record<string, unknown>) => createWhiteboardMock(input),
  };
});

vi.mock("@/lib/search/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/search/client")>();
  return { ...actual, search: (term: string) => searchMock(term) };
});

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <WhiteboardsListPageClient />
    </QueryClientProvider>,
  );
}

describe("WhiteboardsListPageClient", () => {
  beforeEach(() => {
    listWhiteboardsMock.mockReset().mockResolvedValue([]);
    listWhiteboardTemplatesMock.mockReset().mockResolvedValue([]);
    createWhiteboardMock.mockReset();
    searchMock.mockReset();
  });

  it("hides the resource picker until a link type is chosen", async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText("No whiteboards yet")).toBeInTheDocument());
    expect(screen.queryByPlaceholderText(/Search tasks…/)).not.toBeInTheDocument();
  });

  it("creates a whiteboard linked to a task picked via the ResourcePicker, never a raw id", async () => {
    createWhiteboardMock.mockResolvedValue({
      id: "wb-1",
      name: "Sprint planning",
      isPrivate: false,
      ownerUserId: "user-1",
      linkedResourceType: "task",
      linkedResourceId: "task-1",
      isArchived: false,
      updatedAtUtc: new Date().toISOString(),
    } as Whiteboard);
    searchMock.mockResolvedValue([{ type: "Task", id: "task-1", title: "Ship the release" }]);
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByText("No whiteboards yet")).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText(/Link to Task\/Document/), "task");
    const combobox = screen.getByPlaceholderText(/Search tasks…/);
    await user.type(combobox, "ship");
    await waitFor(() => expect(screen.getByText("Ship the release")).toBeInTheDocument());
    await user.click(screen.getByText("Ship the release"));

    // A linked whiteboard's visibility always tracks the linked resource — "Private to me" is disabled.
    expect(screen.getByLabelText("Private to me")).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "New whiteboard" }));

    await waitFor(() =>
      expect(createWhiteboardMock).toHaveBeenCalledWith(
        expect.objectContaining({ linkedResourceType: "task", linkedResourceId: "task-1" }),
      ),
    );
  });
});
