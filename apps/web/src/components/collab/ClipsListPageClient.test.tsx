import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ClipsListPageClient } from "./ClipsListPageClient";
import type { SearchResult } from "@/lib/search/client";
import type { Clip } from "@/lib/collab/types";

const listClipsMock = vi.fn<() => Promise<Clip[]>>();
const uploadClipMock = vi.fn<(input: Record<string, unknown>) => Promise<Clip>>();
const searchMock = vi.fn<(term: string) => Promise<SearchResult[]>>();

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("./clips/useMediaRecorder", () => ({
  useMediaRecorder: () => ({
    isRecording: false,
    error: null,
    start: vi.fn(),
    stop: vi.fn(),
  }),
}));

vi.mock("@/lib/collab/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/collab/client")>();
  return {
    ...actual,
    listClips: () => listClipsMock(),
    uploadClip: (input: Record<string, unknown>) => uploadClipMock(input),
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
      <ClipsListPageClient />
    </QueryClientProvider>,
  );
}

describe("ClipsListPageClient", () => {
  beforeEach(() => {
    listClipsMock.mockReset().mockResolvedValue([]);
    uploadClipMock.mockReset();
    searchMock.mockReset();
  });

  it("hides the resource picker until a link type is chosen", async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText("No clips yet")).toBeInTheDocument());
    expect(screen.queryByPlaceholderText(/Search tasks…/)).not.toBeInTheDocument();
  });

  it("uploads a clip linked to a task picked via the ResourcePicker, never a raw id", async () => {
    uploadClipMock.mockResolvedValue({
      id: "clip-1",
      title: "Standup recap",
      isPrivate: false,
      sizeBytes: 1024,
      status: "Processing",
      linkedResourceType: "task",
      linkedResourceId: "task-1",
      createdAtUtc: new Date().toISOString(),
    } as Clip);
    searchMock.mockResolvedValue([{ type: "Task", id: "task-1", title: "Ship the release" }]);
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByText("No clips yet")).toBeInTheDocument());

    await user.selectOptions(screen.getByLabelText(/Link to Task\/Document/), "task");
    const combobox = screen.getByPlaceholderText(/Search tasks…/);
    await user.type(combobox, "ship");
    await waitFor(() => expect(screen.getByText("Ship the release")).toBeInTheDocument());
    await user.click(screen.getByText("Ship the release"));

    // A linked clip's visibility always tracks the linked resource — "Private to me" is disabled.
    expect(screen.getByLabelText("Private to me")).toBeDisabled();

    const file = new File(["clip-bytes"], "recap.webm", { type: "video/webm" });
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(fileInput, file);

    await waitFor(() =>
      expect(uploadClipMock).toHaveBeenCalledWith(
        expect.objectContaining({ linkedResourceType: "task", linkedResourceId: "task-1" }),
      ),
    );
  });
});
