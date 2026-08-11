"use client";

import { $createCodeNode } from "@lexical/code";
import { INSERT_ORDERED_LIST_COMMAND, INSERT_UNORDERED_LIST_COMMAND } from "@lexical/list";
import { $createLinkNode } from "@lexical/link";
import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import { $createHeadingNode, $createQuoteNode } from "@lexical/rich-text";
import { $setBlocksType } from "@lexical/selection";
import {
  $createParagraphNode,
  $getSelection,
  $isRangeSelection,
  FORMAT_TEXT_COMMAND,
} from "lexical";
import type { ElementNode } from "lexical";
import { useState } from "react";
import { getTask } from "@/lib/work/client";
import { cn } from "@/lib/utils";
import { $createCalloutNode } from "./nodes/CalloutNode";
import { $createTaskReferenceNode } from "./nodes/TaskReferenceNode";

const buttonClass =
  "rounded px-2 py-1 text-xs font-medium text-foreground hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

function ToolbarButton({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <button type="button" className={buttonClass} onMouseDown={(e) => e.preventDefault()} onClick={onClick}>
      {label}
    </button>
  );
}

/**: formatting controls. Bold/italic/underline/headings/lists/code/quote/callout/link, plus
 * (item 4) inserting a task-reference embed by id. */
export function Toolbar({ readOnly }: { readOnly: boolean }) {
  const [editor] = useLexicalComposerContext();
  const [taskIdInput, setTaskIdInput] = useState("");
  const [taskInsertError, setTaskInsertError] = useState<string | null>(null);

  function formatBlock<T extends ElementNode>(create: () => T) {
    editor.update(() => {
      const selection = $getSelection();
      if ($isRangeSelection(selection)) {
        $setBlocksType(selection, create);
      }
    });
  }

  async function insertTaskReference() {
    const taskId = taskIdInput.trim();
    if (!taskId) return;
    setTaskInsertError(null);
    try {
      const task = await getTask(taskId);
      editor.update(() => {
        const selection = $getSelection();
        if ($isRangeSelection(selection)) {
          selection.insertNodes([$createTaskReferenceNode(task.id, task.title)]);
        }
      });
      setTaskIdInput("");
    } catch {
      setTaskInsertError("Task not found.");
    }
  }

  return (
    <div className={cn("flex flex-wrap items-center gap-1 border-b border-border p-2", readOnly && "opacity-50")}>
      <ToolbarButton label="Bold" onClick={() => editor.dispatchCommand(FORMAT_TEXT_COMMAND, "bold")} />
      <ToolbarButton label="Italic" onClick={() => editor.dispatchCommand(FORMAT_TEXT_COMMAND, "italic")} />
      <ToolbarButton label="Underline" onClick={() => editor.dispatchCommand(FORMAT_TEXT_COMMAND, "underline")} />
      <ToolbarButton label="Strikethrough" onClick={() => editor.dispatchCommand(FORMAT_TEXT_COMMAND, "strikethrough")} />
      <span className="mx-1 h-4 w-px bg-border" />
      <ToolbarButton label="H1" onClick={() => formatBlock(() => $createHeadingNode("h1"))} />
      <ToolbarButton label="H2" onClick={() => formatBlock(() => $createHeadingNode("h2"))} />
      <ToolbarButton label="H3" onClick={() => formatBlock(() => $createHeadingNode("h3"))} />
      <ToolbarButton label="Paragraph" onClick={() => formatBlock($createParagraphNode)} />
      <span className="mx-1 h-4 w-px bg-border" />
      <ToolbarButton label="Bullet list" onClick={() => editor.dispatchCommand(INSERT_UNORDERED_LIST_COMMAND, undefined)} />
      <ToolbarButton label="Numbered list" onClick={() => editor.dispatchCommand(INSERT_ORDERED_LIST_COMMAND, undefined)} />
      <span className="mx-1 h-4 w-px bg-border" />
      <ToolbarButton label="Quote" onClick={() => formatBlock($createQuoteNode)} />
      <ToolbarButton label="Callout" onClick={() => formatBlock($createCalloutNode)} />
      <ToolbarButton label="Code block" onClick={() => formatBlock($createCodeNode)} />
      <ToolbarButton
        label="Link"
        onClick={() => {
          const url = window.prompt("Link URL");
          if (!url) return;
          editor.update(() => {
            const selection = $getSelection();
            if ($isRangeSelection(selection)) {
              selection.insertNodes([$createLinkNode(url)]);
            }
          });
        }}
      />
      <span className="mx-1 h-4 w-px bg-border" />
      <div className="flex items-center gap-1">
        <input
          value={taskIdInput}
          onChange={(event) => setTaskIdInput(event.target.value)}
          placeholder="Task id"
          className="h-7 w-32 rounded border border-border bg-background px-2 text-xs"
        />
        <ToolbarButton label="Insert task" onClick={() => void insertTaskReference()} />
        {taskInsertError ? <span className="text-xs text-red-600">{taskInsertError}</span> : null}
      </div>
    </div>
  );
}
