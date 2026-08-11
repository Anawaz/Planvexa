"use client";

// @-mention typeahead: Lexical's own official pattern (LexicalTypeaheadMenuPlugin +
// useBasicTypeaheadTriggerMatch) fed by the same member directory hook the Tiptap comment/description
// editor's mention picker uses (see tiptap/mentionSuggestion.ts) — kept as a separate small menu
// component rather than reusing MentionList.tsx since Lexical's render-prop menu API and Tiptap's
// imperative-handle one don't share a shape.
import { useCallback, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { useLexicalComposerContext } from "@lexical/react/LexicalComposerContext";
import {
  LexicalTypeaheadMenuPlugin,
  MenuOption,
  useBasicTypeaheadTriggerMatch,
} from "@lexical/react/LexicalTypeaheadMenuPlugin";
import type { TextNode } from "lexical";
import { Avatar } from "@/components/ui/Avatar";
import { useCurrentUserId, useMemberDirectory, useMembers } from "@/lib/members";
import { $createMentionNode } from "./nodes/MentionNode";

class MentionOption extends MenuOption {
  userId: string;
  name: string;
  initials: string;
  avatarUrl: string | null;

  constructor(userId: string, name: string, initials: string, avatarUrl: string | null) {
    super(userId);
    this.userId = userId;
    this.name = name;
    this.initials = initials;
    this.avatarUrl = avatarUrl;
  }
}

export function MentionsPlugin() {
  const [editor] = useLexicalComposerContext();
  const { data: members } = useMembers();
  const directory = useMemberDirectory();
  const currentUserId = useCurrentUserId();
  const [queryString, setQueryString] = useState<string | null>(null);

  const checkForMentionMatch = useBasicTypeaheadTriggerMatch("@", { minLength: 0 });

  const options = useMemo(() => {
    const query = (queryString ?? "").toLowerCase();
    return (members ?? [])
      .filter((member) => member.userId !== currentUserId)
      .map((member) => new MentionOption(
        member.userId,
        directory.getLabel(member.userId),
        directory.getInitials(member.userId),
        directory.getAvatarUrl(member.userId),
      ))
      .filter((option) => option.name.toLowerCase().includes(query))
      .slice(0, 8);
  }, [members, currentUserId, directory, queryString]);

  const onSelectOption = useCallback(
    (option: MentionOption, nodeToReplace: TextNode | null, closeMenu: () => void) => {
      editor.update(() => {
        const mentionNode = $createMentionNode(option.userId, option.name);
        nodeToReplace?.replace(mentionNode);
        mentionNode.selectNext();
      });
      closeMenu();
    },
    [editor],
  );

  return (
    <LexicalTypeaheadMenuPlugin<MentionOption>
      onQueryChange={setQueryString}
      onSelectOption={onSelectOption}
      triggerFn={checkForMentionMatch}
      options={options}
      menuRenderFn={(anchorElementRef, { selectedIndex, selectOptionAndCleanUp, setHighlightedIndex }) =>
        anchorElementRef.current && options.length
          ? createPortal(
              <div role="listbox" aria-label="Mention a teammate" className="w-64 rounded-xl border border-border bg-card p-1 text-sm shadow-xl">
                {options.map((option, index) => (
                  <button
                    key={option.key}
                    type="button"
                    role="option"
                    aria-selected={selectedIndex === index}
                    className={`flex w-full items-center gap-2 rounded-lg px-2 py-2 text-left hover:bg-muted ${selectedIndex === index ? "bg-muted" : ""}`}
                    onMouseEnter={() => setHighlightedIndex(index)}
                    onMouseDown={(event) => event.preventDefault()}
                    onClick={() => selectOptionAndCleanUp(option)}
                  >
                    <Avatar
                      avatarUrl={option.avatarUrl}
                      initials={option.initials}
                      className="grid size-7 shrink-0 place-items-center rounded-full bg-muted text-xs font-semibold"
                    />
                    <span className="flex-1 truncate">{option.name}</span>
                  </button>
                ))}
              </div>,
              anchorElementRef.current,
            )
          : null
      }
    />
  );
}
