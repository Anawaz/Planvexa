import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DocumentAccessDialog } from "./DocumentAccessDialog";
import type { ResourcePermissionGrant } from "@/lib/collab/types";
import type { SearchResult } from "@/lib/search/client";

const listDocumentPermissionsMock = vi.fn<() => Promise<ResourcePermissionGrant[]>>();
const grantDocumentPermissionMock = vi.fn<
  (documentId: string, principalType: string, principalId: string, level: string) => Promise<ResourcePermissionGrant>
>();
const revokeDocumentPermissionMock = vi.fn<(documentId: string, principalType: string, principalId: string) => Promise<void>>();
const searchMock = vi.fn<(term: string) => Promise<SearchResult[]>>();

vi.mock("@/lib/collab/client", () => ({
  listDocumentPermissions: () => listDocumentPermissionsMock(),
  grantDocumentPermission: (documentId: string, principalType: string, principalId: string, level: string) =>
    grantDocumentPermissionMock(documentId, principalType, principalId, level),
  revokeDocumentPermission: (documentId: string, principalType: string, principalId: string) =>
    revokeDocumentPermissionMock(documentId, principalType, principalId),
}));

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("@/lib/members", () => ({
  useMemberDirectory: () => ({ getLabel: (userId: string) => (userId === "user-1" ? "Ada Lovelace" : userId) }),
  useTeams: () => ({ data: [{ id: "team-1", name: "Platform Team" }] }),
}));

vi.mock("@/lib/search/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/search/client")>();
  return { ...actual, search: (term: string) => searchMock(term) };
});

function grant(overrides: Partial<ResourcePermissionGrant> = {}): ResourcePermissionGrant {
  return {
    id: "grant-1",
    resourceType: "document",
    resourceId: "doc-1",
    principalType: "user",
    principalId: "user-1",
    level: "view",
    grantedByUserId: "owner-1",
    createdAtUtc: "2024-01-01T00:00:00Z",
    ...overrides,
  };
}

function renderDialog(open = true) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onOpenChange = vi.fn();
  render(
    <QueryClientProvider client={queryClient}>
      <DocumentAccessDialog documentId="doc-1" open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("DocumentAccessDialog", () => {
  beforeEach(() => {
    listDocumentPermissionsMock.mockReset();
    grantDocumentPermissionMock.mockReset();
    revokeDocumentPermissionMock.mockReset();
    searchMock.mockReset();
  });

  it("renders nothing when closed", () => {
    listDocumentPermissionsMock.mockResolvedValue([]);
    renderDialog(false);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("shows the empty state when no one has been granted access yet", async () => {
    listDocumentPermissionsMock.mockResolvedValue([]);
    renderDialog();

    await waitFor(() => expect(screen.getByText("No one else has been granted access yet.")).toBeInTheDocument());
  });

  it("lists an existing grant with a resolved name and revokes it", async () => {
    listDocumentPermissionsMock.mockResolvedValue([grant()]);
    revokeDocumentPermissionMock.mockResolvedValue(undefined);
    renderDialog();

    await waitFor(() => expect(screen.getByText("Ada Lovelace")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Revoke" }));

    await waitFor(() => expect(revokeDocumentPermissionMock).toHaveBeenCalledWith("doc-1", "user", "user-1"));
  });

  it("picks a person via the search picker and grants them view access", async () => {
    listDocumentPermissionsMock.mockResolvedValue([]);
    searchMock.mockResolvedValue([{ type: "Member", id: "user-2", title: "Grace Hopper" }]);
    grantDocumentPermissionMock.mockResolvedValue(grant({ principalId: "user-2" }));
    const user = userEvent.setup();
    renderDialog();

    await waitFor(() => expect(screen.getByText("No one else has been granted access yet.")).toBeInTheDocument());
    await user.type(screen.getByPlaceholderText("Search people…"), "grace");
    await waitFor(() => expect(screen.getByText("Grace Hopper")).toBeInTheDocument());
    await user.click(screen.getByText("Grace Hopper"));

    fireEvent.click(screen.getByRole("button", { name: "Grant access" }));

    await waitFor(() => expect(grantDocumentPermissionMock).toHaveBeenCalledWith("doc-1", "user", "user-2", "view"));
  });
});
