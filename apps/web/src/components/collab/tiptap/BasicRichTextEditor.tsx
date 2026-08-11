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
import { Placeholder } from "@tiptap/extensions";
import StarterKit from "@tiptap/starter-kit";
import { useEffect, useRef } from "react";
import { Markdown } from "tiptap-markdown";
import { cn } from "@/lib/utils";
import { useCurrentUserId, useMemberDirectory, useMembers } from "@/lib/members";
import { BasicToolbar } from "./BasicToolbar";
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
}: BasicRichTextEditorProps) {
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
        StarterKit,
        Placeholder.configure({ placeholder: placeholder ?? "" }),
        // Tiptap's extensions array is built once (see the [] deps below) and this closure is only
        // ever invoked later from a suggestion event handler, never during render; the getter just
        // outlives the linter's static reach.
        // eslint-disable-next-line react-hooks/refs
        MentionExtension.configure({ suggestion: createMentionSuggestion(() => membersRef.current) }),
        Markdown.configure({ html: false, transformPastedText: true }),
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

  return (
    <div
      className={cn(
        "rounded-lg border border-border bg-background focus-within:outline focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring",
        !editable && "border-none bg-transparent",
        className,
      )}
    >
      {editable ? <BasicToolbar editor={editor} className="border-b border-border px-2 py-1.5" /> : null}
      <div className="px-3 py-2">
        <EditorContent editor={editor} />
      </div>
    </div>
  );
}
