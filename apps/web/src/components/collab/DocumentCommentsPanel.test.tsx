import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DocumentCommentsPanel } from "./DocumentEditorPageClient";
import type { DocumentComment } from "@/lib/collab/types";

const listDocumentCommentsMock = vi.fn<() => Promise<DocumentComment[]>>();
const addDocumentCommentMock = vi.fn<(documentId: string, body: string) => Promise<DocumentComment>>();

vi.mock("@/lib/collab/client", () => ({
  listDocumentComments: () => listDocumentCommentsMock(),
  addDocumentComment: (documentId: string, body: string) => addDocumentCommentMock(documentId, body),
}));

vi.mock("@/lib/members", () => ({
  useMemberDirectory: () => ({ getLabel: (userId: string) => userId }),
}));

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

function comment(overrides: Partial<DocumentComment> = {}): DocumentComment {
  return {
    id: "comment-1",
    authorUserId: "user-1",
    body: "First thoughts",
    createdAtUtc: new Date().toISOString(),
    ...overrides,
  };
}

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <DocumentCommentsPanel documentId="doc-1" />
    </QueryClientProvider>,
  );
}

describe("DocumentCommentsPanel", () => {
  beforeEach(() => {
    listDocumentCommentsMock.mockReset();
    addDocumentCommentMock.mockReset();
  });

  it("renders the empty state when the document has no comments", async () => {
    listDocumentCommentsMock.mockResolvedValue([]);
    renderPanel();

    await waitFor(() => expect(screen.getByText("No comments yet.")).toBeInTheDocument());
  });

  it("lists existing comments with their author and body", async () => {
    listDocumentCommentsMock.mockResolvedValue([comment()]);
    renderPanel();

    await waitFor(() => expect(screen.getByText("First thoughts")).toBeInTheDocument());
    expect(screen.getByText("user-1")).toBeInTheDocument();
  });

  it("posts a new comment and clears the input on success", async () => {
    listDocumentCommentsMock.mockResolvedValue([]);
    addDocumentCommentMock.mockResolvedValue(comment({ id: "comment-2", body: "Nice work" }));
    renderPanel();

    await waitFor(() => expect(screen.getByText("No comments yet.")).toBeInTheDocument());

    const input = screen.getByPlaceholderText("Add a comment") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "Nice work" } });
    fireEvent.click(screen.getByRole("button", { name: "Post" }));

    await waitFor(() => expect(addDocumentCommentMock).toHaveBeenCalledWith("doc-1", "Nice work"));
    await waitFor(() => expect(input.value).toBe(""));
  });

  it("does not submit a blank comment", async () => {
    listDocumentCommentsMock.mockResolvedValue([]);
    renderPanel();

    await waitFor(() => expect(screen.getByText("No comments yet.")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Post" }));

    expect(addDocumentCommentMock).not.toHaveBeenCalled();
  });
});
