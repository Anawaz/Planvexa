"use client";

import { useRef } from "react";
import { $createCodeNode } from "@lexical/code";
import {
  INSERT_CHECK_LIST_COMMAND,
  INSERT_ORDERED_LIST_COMMAND,
  INSERT_UNORDERED_LIST_COMMAND,
} from "@lexical/list";
import { $createLinkNode } from "@lexical/link";
import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import { $createHeadingNode, $createQuoteNode } from "@lexical/rich-text";
import { INSERT_TABLE_COMMAND } from "@lexical/table";
import { $setBlocksType } from "@lexical/selection";
import {
  $createParagraphNode,
  $getSelection,
  $isRangeSelection,
  FORMAT_TEXT_COMMAND,
} from "lexical";
import type { ElementNode } from "lexical";
import { ResourcePicker } from "@/components/ui/ResourcePicker";
import { cn } from "@/lib/utils";
import { $createCalloutNode } from "./nodes/CalloutNode";
import { insertFileAttachmentFromFile } from "./insertFileAttachment";
import { insertImageFromFile } from "./insertImage";
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

/**: formatting controls. Bold/italic/underline/headings/lists (bullet/numbered/check)/code/quote/
 * callout/link/image/file-attachment, plus (item 4) inserting a task-reference embed by id. */
export function Toolbar({ readOnly, documentId }: { readOnly: boolean; documentId: string }) {
  const [editor] = useLexicalComposerContext();
  const imageInputRef = useRef<HTMLInputElement>(null);
  const attachmentInputRef = useRef<HTMLInputElement>(null);

  function formatBlock<T extends ElementNode>(create: () => T) {
    editor.update(() => {
      const selection = $getSelection();
      if ($isRangeSelection(selection)) {
        $setBlocksType(selection, create);
      }
    });
  }

  function insertTaskReference(taskId: string, title: string) {
    editor.update(() => {
      const selection = $getSelection();
      if ($isRangeSelection(selection)) {
        selection.insertNodes([$createTaskReferenceNode(taskId, title)]);
      }
    });
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
      <ToolbarButton label="Checklist" onClick={() => editor.dispatchCommand(INSERT_CHECK_LIST_COMMAND, undefined)} />
      <span className="mx-1 h-4 w-px bg-border" />
      <ToolbarButton label="Quote" onClick={() => formatBlock($createQuoteNode)} />
      <ToolbarButton label="Callout" onClick={() => formatBlock($createCalloutNode)} />
      <ToolbarButton label="Code block" onClick={() => formatBlock($createCodeNode)} />
      <ToolbarButton
        label="Table"
        onClick={() =>
          editor.dispatchCommand(INSERT_TABLE_COMMAND, { columns: "3", rows: "3", includeHeaders: true })
        }
      />
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
      <ToolbarButton label="Image" onClick={() => imageInputRef.current?.click()} />
      <input
        ref={imageInputRef}
        type="file"
        accept="image/*"
        className="hidden"
        onChange={(e) => {
          const file = e.target.files?.[0];
          e.target.value = "";
          if (file) void insertImageFromFile(editor, documentId, file);
        }}
      />
      <ToolbarButton label="Attach file" onClick={() => attachmentInputRef.current?.click()} />
      <input
        ref={attachmentInputRef}
        type="file"
        className="hidden"
        onChange={(e) => {
          const file = e.target.files?.[0];
          e.target.value = "";
          if (file) void insertFileAttachmentFromFile(editor, documentId, file);
        }}
      />
      <span className="mx-1 h-4 w-px bg-border" />
      <div className="w-40">
        <ResourcePicker
          types={["Task"]}
          value=""
          onChange={(taskId, title) => {
            if (taskId) insertTaskReference(taskId, title);
          }}
          placeholder="Insert task…"
        />
      </div>
    </div>
  );
}
