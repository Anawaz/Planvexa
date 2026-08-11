import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { LexicalComposer } from "@lexical/react/LexicalComposer";
import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import { ContentEditable } from "@lexical/react/LexicalContentEditable";
import { LexicalErrorBoundary } from "@lexical/react/LexicalErrorBoundary";
import { RichTextPlugin } from "@lexical/react/LexicalRichTextPlugin";
import { $getRoot, $getSelection, $isElementNode, $isRangeSelection } from "lexical";
import type { LexicalEditor } from "lexical";
import { editorNodes, editorTheme } from "./editorConfig";
import { MentionsPlugin } from "./MentionsPlugin";
import { $isMentionNode } from "./nodes/MentionNode";

vi.mock("@/lib/members", () => ({
  useMembers: () => ({
    data: [
      { userId: "user-1", displayName: "Ada Lovelace" },
      { userId: "user-2", displayName: "Grace Hopper" },
    ],
  }),
  useMemberDirectory: () => ({
    getLabel: (userId: string) => (userId === "user-1" ? "Ada Lovelace" : "Grace Hopper"),
    getInitials: (userId: string) => (userId === "user-1" ? "AL" : "GH"),
    getAvatarUrl: () => null,
  }),
  useCurrentUserId: () => "current-user",
}));

function EditorCapture({ onReady }: { onReady: (editor: LexicalEditor) => void }) {
  const [editor] = useLexicalComposerContext();
  onReady(editor);
  return null;
}

function renderEditor() {
  let editor!: LexicalEditor;
  render(
    <LexicalComposer
      initialConfig={{
        namespace: "mentions-test",
        theme: editorTheme,
        nodes: [...editorNodes],
        onError: (error) => {
          throw error;
        },
      }}
    >
      <RichTextPlugin
        contentEditable={<ContentEditable aria-label="Document content" />}
        placeholder={null}
        ErrorBoundary={LexicalErrorBoundary}
      />
      <MentionsPlugin />
      <EditorCapture onReady={(e) => (editor = e)} />
    </LexicalComposer>,
  );
  return editor;
}

function typeAtCursor(editor: LexicalEditor, text: string) {
  act(() => {
    editor.update(() => {
      $getRoot().selectEnd();
      const selection = $getSelection();
      if ($isRangeSelection(selection)) {
        selection.insertText(text);
      }
    });
  });
}

// jsdom implements neither Range.getBoundingClientRect/getClientRects nor ResizeObserver, both of
// which the typeahead menu plugin uses to position and keep its popup aligned — stub them so
// mounting the plugin doesn't throw in tests.
beforeAll(() => {
  Range.prototype.getBoundingClientRect = () => new DOMRect();
  Range.prototype.getClientRects = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

describe("MentionsPlugin", () => {
  it("opens a member picker on '@' and inserts a mention node on selection", async () => {
    const editor = renderEditor();

    typeAtCursor(editor, "@ada");

    const option = await screen.findByRole("option", { name: /Ada Lovelace/ });
    await userEvent.click(option);

    await waitFor(() => {
      let inserted = false;
      editor.getEditorState().read(() => {
        const paragraph = $getRoot().getFirstChild();
        inserted = Boolean(paragraph && $isElementNode(paragraph) && paragraph.getChildren().some((node) => $isMentionNode(node)));
      });
      expect(inserted).toBe(true);
    });
  });

  it("does not show teammates who don't match the typed query", async () => {
    const editor = renderEditor();

    typeAtCursor(editor, "@grace");

    await screen.findByRole("option", { name: /Grace Hopper/ });
    expect(screen.queryByRole("option", { name: /Ada Lovelace/ })).not.toBeInTheDocument();
  });
});
