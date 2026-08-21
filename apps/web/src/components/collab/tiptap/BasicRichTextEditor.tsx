"use client";

// The rich-text editor for task descriptions and comments: Tiptap/ProseMirror (the same engine behind
// GitLab's Content Editor), not the Lexical editor Documents uses — Documents needs Yjs-collaborative
// structured editing; these fields save once (comments) or on blur (description), so a local,
// non-collaborative editor is the right fit. Content round-trips as markdown via tiptap-markdown
// (ponytail: that package no longer takes new releases upstream — it still works fine against Tiptap
// v3, see mentionExtension.ts's custom node hook; fork or hand-roll a serializer if it ever breaks),
// so it stays a plain string in the existing description/comment-body columns — no backend changes,
// and every existing plain-text description/comment is already valid markdown.
import { EditorContent, useEditor } from "@tiptap/react";
import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import { useCurrentUserId, useMemberDirectory, useMembers } from "@/lib/members";
import { BasicToolbar, EditorFooter } from "./BasicToolbar";
import { createEditorExtensions } from "./editorExtensions";
import { MentionExtension } from "./mentionExtension";
import type { MentionListItem } from "./MentionList";
import { createMentionSuggestion } from "./mentionSuggestion";

export type BasicRichTextEditorProps = {
  /** Initial markdown content. Uncontrolled after mount, like a textarea's defaultValue — callers
   * that need to reset it (a new task, a cleared comment box) remount via a `key` change. */
  value: string;
  onChange?: (markdown: string, mentionUserIds: string[]) => void;
  placeholder?: string;
  editable?: boolean;
  autoFocus?: boolean;
  ariaLabel: string;
  minHeightClassName?: string;
  className?: string;
  /** Wired to the caller's existing file-upload control; the paperclip is hidden when absent. */
  onAttachFile?: () => void;
};

export function BasicRichTextEditor({
  value,
  onChange,
  placeholder,
  editable = true,
  autoFocus = false,
  ariaLabel,
  minHeightClassName = "min-h-24",
  className,
  onAttachFile,
}: BasicRichTextEditorProps) {
  // The plain-text escape hatch. Raw markdown is kept in its own state while the mode is on and pushed
  // into the editor when switching back, so the two views never fight over one source of truth.
  const [plainText, setPlainText] = useState(false);
  const [plainTextValue, setPlainTextValue] = useState(value);
  const { data: members } = useMembers();
  const directory = useMemberDirectory();
  const currentUserId = useCurrentUserId();
  // Suggestion's `items()` is captured once when the extension is created (below), so it reads
  // through this ref rather than closing over a stale `members` snapshot. Only ever read from an
  // event handler (typing "@"), so updating it a tick after render (in an effect) is fine.
  const membersRef = useRef<MentionListItem[]>([]);
  useEffect(() => {
    membersRef.current = (members ?? [])
      .filter((member) => member.userId !== currentUserId)
      .map((member) => ({
        id: member.userId,
        name: directory.getLabel(member.userId),
        initials: directory.getInitials(member.userId),
        avatarUrl: directory.getAvatarUrl(member.userId),
      }));
  }, [members, currentUserId, directory]);

  const editor = useEditor(
    {
      extensions: [
        ...createEditorExtensions(placeholder),
        // Tiptap's extensions array is built once (see the [] deps below) and this closure is only
        // ever invoked later from a suggestion event handler, never during render; the getter just
        // outlives the linter's static reach.
        // eslint-disable-next-line react-hooks/refs
        MentionExtension.configure({ suggestion: createMentionSuggestion(() => membersRef.current) }),
      ],
      content: value,
      editable,
      autofocus: autoFocus,
      immediatelyRender: false,
      editorProps: {
        attributes: {
          // Unlike Lexical's ContentEditable, Tiptap's doesn't set an implicit textbox role/name on
          // its contenteditable div — without these it's invisible to the accessibility tree (and to
          // Playwright's getByRole("textbox", { name })) despite the aria-label being present.
          role: "textbox",
          "aria-multiline": "true",
          "aria-label": ariaLabel,
          class: cn("prose prose-sm max-w-none text-sm leading-6 outline-none", minHeightClassName),
        },
      },
      onUpdate: ({ editor: current }) => {
        if (!onChange) return;
        const markdown = current.storage.markdown.getMarkdown() as string;
        const mentionUserIds = new Set<string>();
        current.state.doc.descendants((node) => {
          if (node.type.name === "mention" && typeof node.attrs.id === "string") {
            mentionUserIds.add(node.attrs.id);
          }
        });
        onChange(markdown, [...mentionUserIds]);
      },
    },
    [],
  );

  if (!editor) {
    return null;
  }

  // The null guard above already returned, but that narrowing does not reach into the closure below.
  const activeEditor = editor;

  function togglePlainText() {
    if (plainText) {
      // Back to rich text: parse whatever was typed as markdown, and re-emit so the caller's draft
      // matches what the editor now holds.
      activeEditor.commands.setContent(plainTextValue);
      onChange?.(activeEditor.storage.markdown.getMarkdown() as string, []);
      setPlainText(false);
      return;
    }

    setPlainTextValue(activeEditor.storage.markdown.getMarkdown() as string);
    setPlainText(true);
  }

  return (
    <div
      className={cn(
        "rounded-lg border border-border bg-background focus-within:outline focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring",
        !editable && "border-none bg-transparent",
        className,
      )}
    >
      {editable && !plainText ? (
        <BasicToolbar
          editor={editor}
          onAttachFile={onAttachFile}
          className="border-b border-border px-2 py-1.5"
        />
      ) : null}

      <div className={cn("px-3 py-2", plainText && "hidden")}>
        <EditorContent editor={editor} />
      </div>

      {editable && plainText ? (
        <textarea
          aria-label={`${ariaLabel} (markdown)`}
          value={plainTextValue}
          onChange={(event) => {
            setPlainTextValue(event.target.value);
            // Emitted as-is: in this mode what the user typed IS the markdown, so there is nothing to
            // serialize. Mentions cannot be resolved from raw text, hence the empty id list.
            onChange?.(event.target.value, []);
          }}
          className={cn(
            "w-full resize-y bg-transparent px-3 py-2 font-mono text-sm leading-6 outline-none",
            minHeightClassName,
          )}
        />
      ) : null}

      {editable ? <EditorFooter plainText={plainText} onTogglePlainText={togglePlainText} /> : null}
    </div>
  );
}
