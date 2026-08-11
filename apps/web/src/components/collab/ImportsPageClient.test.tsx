import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ImportsPageClient } from "./ImportsPageClient";
import { ApiError } from "@/lib/api-client";
import type { ImportJob, ImportJobRow } from "@/lib/collab/types";

const listImportSourcesMock = vi.fn<() => Promise<string[]>>();
const listImportJobsMock = vi.fn<() => Promise<ImportJob[]>>();
const listImportJobRowsMock = vi.fn<() => Promise<ImportJobRow[]>>();
const uploadImportJobMock = vi.fn<(input: Record<string, unknown>) => Promise<ImportJob>>();

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("@/lib/collab/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/collab/client")>();
  return {
    ...actual,
    listImportSources: () => listImportSourcesMock(),
    listImportJobs: () => listImportJobsMock(),
    listImportJobRows: () => listImportJobRowsMock(),
    uploadImportJob: (input: Record<string, unknown>) => uploadImportJobMock(input),
  };
});

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <ImportsPageClient />
    </QueryClientProvider>,
  );
}

describe("ImportsPageClient", () => {
  beforeEach(() => {
    listImportSourcesMock.mockReset().mockResolvedValue(["Csv", "Xlsx", "Trello", "Jira", "Asana", "ClickUp"]);
    listImportJobsMock.mockReset().mockResolvedValue([]);
    listImportJobRowsMock.mockReset().mockResolvedValue([]);
    uploadImportJobMock.mockReset();
  });

  it("shows the backend's validation message when uploading an unimplemented source type fails", async () => {
    uploadImportJobMock.mockRejectedValue(
      new ApiError("ClickUp import is not yet implemented — needs ClickUp's task export format.", 400, undefined),
    );
    const user = userEvent.setup();
    renderPage();

    await waitFor(() => expect(screen.getByRole("option", { name: "ClickUp" })).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText("Source type"), "ClickUp");

    const file = new File(["board"], "board.csv", { type: "text/csv" });
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(fileInput, file);
    await user.click(screen.getByRole("button", { name: "Upload" }));

    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent(
        "ClickUp import is not yet implemented — needs ClickUp's task export format.",
      ),
    );
  });
});
