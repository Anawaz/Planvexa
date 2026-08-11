import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { LexicalComposer } from "@lexical/react/LexicalComposer";
import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import { ContentEditable } from "@lexical/react/LexicalContentEditable";
import { LexicalErrorBoundary } from "@lexical/react/LexicalErrorBoundary";
import { RichTextPlugin } from "@lexical/react/LexicalRichTextPlugin";
import { TablePlugin } from "@lexical/react/LexicalTablePlugin";
import { CheckListPlugin } from "@lexical/react/LexicalCheckListPlugin";
import { $isTableNode } from "@lexical/table";
import { $isListNode } from "@lexical/list";
import { $getRoot } from "lexical";
import type { LexicalEditor } from "lexical";
import { editorNodes, editorTheme } from "./editorConfig";
import { $isFileAttachmentNode } from "./nodes/FileAttachmentNode";
import { $isImageNode } from "./nodes/ImageNode";
import { Toolbar } from "./Toolbar";

const uploadDocumentImageMock = vi.fn();
const uploadDocumentAttachmentMock = vi.fn();

vi.mock("@/lib/collab/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/collab/client")>();
  return {
    ...actual,
    uploadDocumentImage: (...args: unknown[]) => uploadDocumentImageMock(...args),
    uploadDocumentAttachment: (...args: unknown[]) => uploadDocumentAttachmentMock(...args),
  };
});

function EditorCapture({ onReady }: { onReady: (editor: LexicalEditor) => void }) {
  const [editor] = useLexicalComposerContext();
  onReady(editor);
  return null;
}

function renderToolbar() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  let editor!: LexicalEditor;
  render(
    <QueryClientProvider client={queryClient}>
      <LexicalComposer
        initialConfig={{
          namespace: "toolbar-test",
          theme: editorTheme,
          nodes: [...editorNodes],
          onError: (error) => {
            throw error;
          },
        }}
      >
        <Toolbar readOnly={false} documentId="doc-1" />
        <RichTextPlugin
          contentEditable={<ContentEditable aria-label="Document content" />}
          placeholder={null}
          ErrorBoundary={LexicalErrorBoundary}
        />
        <TablePlugin />
        <CheckListPlugin />
        <EditorCapture
          onReady={(e) => {
            editor = e;
          }}
        />
      </LexicalComposer>
    </QueryClientProvider>,
  );
  return editor;
}

describe("Toolbar", () => {
  beforeEach(() => {
    uploadDocumentImageMock.mockReset();
    uploadDocumentAttachmentMock.mockReset();
  });

  it("uploads the selected file and inserts an image node when the Image button is used", async () => {
    uploadDocumentImageMock.mockResolvedValue({ imageId: "img-1", contentType: "image/png" });
    const editor = renderToolbar();
    const user = userEvent.setup();

    await user.click(screen.getByLabelText("Document content"));
    const file = new File(["fake-bytes"], "photo.png", { type: "image/png" });
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(input, file);

    expect(uploadDocumentImageMock).toHaveBeenCalledWith("doc-1", file);

    await waitFor(() => {
      let hasImage = false;
      editor.getEditorState().read(() => {
        hasImage = $getRoot()
          .getChildren()
          .some((node) => $isImageNode(node));
      });

      expect(hasImage).toBe(true);
    });
  });

  it("uploads the selected file and inserts a file-attachment node when the Attach file button is used", async () => {
    uploadDocumentAttachmentMock.mockResolvedValue({
      attachmentId: "att-1",
      fileName: "report.pdf",
      contentType: "application/pdf",
      sizeBytes: 1234,
    });
    const editor = renderToolbar();
    const user = userEvent.setup();

    await user.click(screen.getByLabelText("Document content"));
    const file = new File(["fake-bytes"], "report.pdf", { type: "application/pdf" });
    // The attachment input has no `accept` filter (unlike the image input above), which distinguishes it.
    const input = document.querySelector('input[type="file"]:not([accept])') as HTMLInputElement;
    await user.upload(input, file);

    expect(uploadDocumentAttachmentMock).toHaveBeenCalledWith("doc-1", file);

    await waitFor(() => {
      let hasAttachment = false;
      editor.getEditorState().read(() => {
        hasAttachment = $getRoot()
          .getChildren()
          .some((node) => $isFileAttachmentNode(node));
      });

      expect(hasAttachment).toBe(true);
    });
  });

  it("inserts a table node when the Table button is clicked", async () => {
    const editor = renderToolbar();
    const user = userEvent.setup();

    // A selection must exist for the table insert command to know where to insert (mirrors the real
    // editor, where the ContentEditable always has focus/selection once the user starts typing).
    await user.click(screen.getByLabelText("Document content"));
    await user.click(screen.getByRole("button", { name: "Table" }));

    let hasTable = false;
    editor.getEditorState().read(() => {
      hasTable = $getRoot()
        .getChildren()
        .some((node) => $isTableNode(node));
    });

    expect(hasTable).toBe(true);
  });

  it("inserts a check list when the Checklist button is clicked", async () => {
    const editor = renderToolbar();
    const user = userEvent.setup();

    await user.click(screen.getByLabelText("Document content"));
    await user.click(screen.getByRole("button", { name: "Checklist" }));

    let listType: string | null = null;
    editor.getEditorState().read(() => {
      const listNode = $getRoot()
        .getChildren()
        .find((node) => $isListNode(node));
      listType = listNode && $isListNode(listNode) ? listNode.getListType() : null;
    });

    expect(listType).toBe("check");
  });
});
