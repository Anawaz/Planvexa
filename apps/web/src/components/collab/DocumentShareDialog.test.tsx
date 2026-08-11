import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DocumentShareDialog } from "./DocumentShareDialog";
import type { DocumentShareLink } from "@/lib/collab/types";

const createDocumentShareMock = vi.fn<(documentId: string, days?: number, password?: string) => Promise<DocumentShareLink>>();
const listDocumentSharesMock = vi.fn<() => Promise<DocumentShareLink[]>>();
const revokeDocumentShareMock = vi.fn<(id: string) => Promise<void>>();

vi.mock("@/lib/collab/client", () => ({
  createDocumentShare: (documentId: string, days?: number, password?: string) =>
    createDocumentShareMock(documentId, days, password),
  listDocumentShares: () => listDocumentSharesMock(),
  revokeDocumentShare: (id: string) => revokeDocumentShareMock(id),
}));

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

function share(overrides: Partial<DocumentShareLink> = {}): DocumentShareLink {
  return {
    id: "share-1",
    documentId: "doc-1",
    token: "",
    url: "/public/documents/abc123",
    expiresAtUtc: null,
    requiresPassword: false,
    ...overrides,
  };
}

function renderDialog(open = true) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onOpenChange = vi.fn();
  render(
    <QueryClientProvider client={queryClient}>
      <DocumentShareDialog documentId="doc-1" open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("DocumentShareDialog", () => {
  beforeEach(() => {
    createDocumentShareMock.mockReset();
    listDocumentSharesMock.mockReset();
    revokeDocumentShareMock.mockReset();
  });

  it("renders nothing when closed", () => {
    listDocumentSharesMock.mockResolvedValue([]);
    renderDialog(false);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("shows the empty state when the document has no links yet", async () => {
    listDocumentSharesMock.mockResolvedValue([]);
    renderDialog();

    await waitFor(() => expect(screen.getByText("No public links yet.")).toBeInTheDocument());
  });

  it("creates a share link and shows the one-time token URL", async () => {
    listDocumentSharesMock.mockResolvedValue([]);
    createDocumentShareMock.mockResolvedValue(share());
    renderDialog();

    await waitFor(() => expect(screen.getByText("No public links yet.")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Create share link" }));

    await waitFor(() => expect(createDocumentShareMock).toHaveBeenCalledWith("doc-1", 7, ""));
    await waitFor(() => expect(screen.getByText(/public\/documents\/abc123/)).toBeInTheDocument());
  });

  it("lists an existing link with its token redacted and revokes it", async () => {
    listDocumentSharesMock.mockResolvedValue([share()]);
    revokeDocumentShareMock.mockResolvedValue(undefined);
    renderDialog();

    await waitFor(() => expect(screen.getByText("/public/documents/abc123")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Revoke" }));

    await waitFor(() => expect(revokeDocumentShareMock).toHaveBeenCalledWith("share-1"));
  });
});
