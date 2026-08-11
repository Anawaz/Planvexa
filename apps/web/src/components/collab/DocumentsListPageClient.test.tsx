import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DocumentsListPageClient } from "./DocumentsListPageClient";
import type { DocumentSummary, DocumentTemplate } from "@/lib/collab/types";

const listDocumentsMock = vi.fn<() => Promise<DocumentSummary[]>>();
const listDocumentTemplatesMock = vi.fn<() => Promise<DocumentTemplate[]>>();

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("@/lib/collab/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/collab/client")>();
  return {
    ...actual,
    listDocuments: () => listDocumentsMock(),
    listDocumentTemplates: () => listDocumentTemplatesMock(),
  };
});

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <DocumentsListPageClient />
    </QueryClientProvider>,
  );
}

describe("DocumentsListPageClient loading/error/empty states", () => {
  beforeEach(() => {
    listDocumentsMock.mockReset();
    listDocumentTemplatesMock.mockReset().mockResolvedValue([]);
  });

  it("shows a genuine error state (not the empty state) when the documents query rejects", async () => {
    listDocumentsMock.mockRejectedValue(new Error("boom"));
    renderPage();

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument());
    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
    expect(screen.queryByText("No documents yet")).not.toBeInTheDocument();
  });

  it("shows the empty state (not an error) when the documents query resolves with no documents", async () => {
    listDocumentsMock.mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(screen.getByText("No documents yet")).toBeInTheDocument());
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
