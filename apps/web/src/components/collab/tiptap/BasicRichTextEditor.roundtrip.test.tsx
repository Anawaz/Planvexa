import { Editor } from "@tiptap/core";
import { describe, expect, it } from "vitest";
import { createEditorExtensions } from "./editorExtensions";

/**
 * Descriptions and comments are stored as a markdown STRING, and rendered by re-mounting the editor
 * read-only. So every toolbar control is only as good as its markdown round trip: a node Tiptap can
 * create but tiptap-markdown cannot serialize is silent data loss on save, and one it cannot parse
 * back is data loss on the next edit.
 *
 * Driven headlessly against the real extension list (createEditorExtensions, the same one the
 * component uses) rather than through the rendered component — ProseMirror's DOM measurement needs
 * layout APIs jsdom does not implement, and none of that is what these assertions are about.
 */
function roundTrip(markdown: string): string {
  const editor = new Editor({ extensions: createEditorExtensions(), content: markdown });
  try {
    return editor.storage.markdown.getMarkdown() as string;
  } finally {
    editor.destroy();
  }
}

describe("markdown round trip", () => {
  it("preserves a task list, including which items are checked", () => {
    const result = roundTrip("- [ ] not done\n- [x] done");

    expect(result).toContain("not done");
    expect(result).toContain("done");
    // A checkbox that silently reset to unchecked on every save would be worse than no checkbox.
    expect(result).toContain("[x]");
    expect(result).toContain("[ ]");
  });

  it("preserves a table's cells and header separator", () => {
    const result = roundTrip("| a | b |\n| --- | --- |\n| 1 | 2 |");

    for (const cell of ["a", "b", "1", "2"]) {
      expect(result).toContain(cell);
    }
    // Without the delimiter row it is no longer a table when parsed back.
    expect(result).toMatch(/\|\s*-+\s*\|/);
  });

  it("preserves a fenced code block and its language tag", () => {
    // Mermaid and PlantUML are stored exactly this way — as fenced blocks with a language tag, which
    // is what GitLab stores too. Losing the tag would turn a diagram into anonymous code.
    expect(roundTrip("```mermaid\ngraph TD;\n  A-->B;\n```")).toContain("```mermaid");
    expect(roundTrip("```plantuml\n@startuml\nAlice -> Bob: hello\n@enduml\n```")).toContain("```plantuml");
    expect(roundTrip("```js\nconst a = 1;\n```")).toContain("const a = 1;");
  });

  it("cannot carry a GFM alert marker — which is why there is no Alert button", () => {
    // Regression guard on a deliberate omission. The serializer escapes the marker and folds the line
    // break, so `> [!NOTE]` degrades to an ordinary quote. If this ever starts passing, tiptap-markdown
    // has gained alert support and the Alert control can be added back.
    const result = roundTrip("> [!NOTE]\n> Something worth knowing");

    expect(result).not.toContain("[!NOTE]");
    expect(result).toContain("Something worth knowing");
  });

  it("preserves headings, quotes and horizontal rules", () => {
    expect(roundTrip("## A heading")).toContain("## A heading");
    expect(roundTrip("> quoted")).toContain("> quoted");
    expect(roundTrip("before\n\n---\n\nafter")).toMatch(/before[\s\S]*---[\s\S]*after/);
  });

  it("preserves inline marks", () => {
    const result = roundTrip("**bold** _italic_ ~~struck~~ `code` [link](https://example.test)");

    expect(result).toContain("**bold**");
    expect(result).toContain("~~struck~~");
    expect(result).toContain("`code`");
    expect(result).toContain("https://example.test");
  });

  it("drops nothing when the whole toolbar's output is combined", () => {
    // The realistic worst case: one document using every block the toolbar can produce. Anything the
    // serializer cannot handle shows up here as a missing marker rather than as a support ticket.
    const document = [
      "# Title",
      "",
      "Some **text** with `code`.",
      "",
      "> [!NOTE]",
      "> An alert",
      "",
      "- bullet",
      "",
      "1. ordered",
      "",
      "- [x] task",
      "",
      "| a | b |",
      "| --- | --- |",
      "| 1 | 2 |",
      "",
      "```mermaid",
      "graph TD;",
      "```",
      "",
      "---",
    ].join("\n");

    const result = roundTrip(document);

    for (const marker of ["# Title", "**text**", "bullet", "ordered", "[x]", "```mermaid"]) {
      expect(result).toContain(marker);
    }
  });
});
