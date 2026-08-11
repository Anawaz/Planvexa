import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { CommentComposer } from "./CommentComposer";

vi.mock("@/lib/app-context/AppContext", () => ({
  useAppContext: () => ({ workspaceId: "ws-1" }),
}));

vi.mock("@/lib/realtime/useRealtime", () => ({
  useTypingBroadcast: () => () => {},
}));

// The rich text editor is Tiptap/ProseMirror and covered by its own tests; stub it here so this
// test can stay focused on the file-attach behaviour, driving it through the same onChange contract.
vi.mock("./tiptap/BasicRichTextEditor", () => ({
  BasicRichTextEditor: ({ onChange }: { onChange?: (markdown: string, mentions: string[]) => void }) => (
    <textarea aria-label="comment-body" onChange={(event) => onChange?.(event.target.value, [])} />
  ),
}));

describe("CommentComposer file drop", () => {
  it("attaches a file dropped onto the composer, same as the file input", () => {
    render(<CommentComposer taskId="task-1" onSubmit={vi.fn()} />);

    const form = screen.getByText("New comment").closest("form")!;
    const file = new File(["hello"], "hello.txt", { type: "text/plain" });

    fireEvent.drop(form, { dataTransfer: { types: ["Files"], files: [file] } });

    expect(screen.getByText("hello.txt")).toBeInTheDocument();
  });

  it("submits the dropped file the same way a chosen file would be submitted", async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<CommentComposer taskId="task-1" onSubmit={onSubmit} />);

    fireEvent.change(screen.getByLabelText("comment-body"), { target: { value: "Looks good" } });

    const form = screen.getByText("New comment").closest("form")!;
    const file = new File(["hello"], "hello.txt", { type: "text/plain" });
    fireEvent.drop(form, { dataTransfer: { types: ["Files"], files: [file] } });

    fireEvent.click(screen.getByRole("button", { name: "Comment" }));

    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ taskId: "task-1", body: "Looks good", file }),
    );
  });

  it("shows a drag-over highlight while a file is dragged over the composer", () => {
    render(<CommentComposer taskId="task-1" onSubmit={vi.fn()} />);
    const form = screen.getByText("New comment").closest("form")!;

    fireEvent.dragEnter(form, { dataTransfer: { types: ["Files"], files: [] } });
    expect(form.className).toContain("border-primary");

    fireEvent.dragLeave(form, { dataTransfer: { types: ["Files"], files: [] } });
    expect(form.className).not.toContain("border-primary");
  });
});
