import { forwardRef, useEffect, useImperativeHandle, useState } from "react";
import { Avatar } from "@/components/ui/Avatar";

export type MentionListItem = { id: string; name: string; initials: string; avatarUrl?: string | null };

export type MentionListHandle = {
  onKeyDown: (props: { event: KeyboardEvent }) => boolean;
};

type MentionListProps = {
  items: MentionListItem[];
  command: (item: MentionListItem) => void;
};

/** The floating @mention picker, rendered via ReactRenderer + Suggestion's `mount()` (see
 * mentionSuggestion.ts) — arrow keys/Enter handled through the imperative handle Tiptap's
 * suggestion plugin calls directly, since keydown happens on the editor, not this component. */
export const MentionList = forwardRef<MentionListHandle, MentionListProps>(({ items, command }, ref) => {
  const [selectedIndex, setSelectedIndex] = useState(0);

  useEffect(() => {
    setSelectedIndex(0);
  }, [items]);

  function selectItem(index: number) {
    const item = items[index];
    if (item) {
      command(item);
    }
  }

  useImperativeHandle(ref, () => ({
    onKeyDown({ event }) {
      if (event.key === "ArrowUp") {
        setSelectedIndex((current) => (current + items.length - 1) % items.length);
        return true;
      }
      if (event.key === "ArrowDown") {
        setSelectedIndex((current) => (current + 1) % items.length);
        return true;
      }
      if (event.key === "Enter") {
        selectItem(selectedIndex);
        return true;
      }
      return false;
    },
  }));

  if (items.length === 0) {
    return null;
  }

  return (
    <div
      role="listbox"
      aria-label="Mention a teammate"
      className="w-64 rounded-xl border border-border bg-card p-1 text-sm shadow-xl"
    >
      {items.map((item, index) => (
        <button
          key={item.id}
          type="button"
          role="option"
          aria-selected={index === selectedIndex}
          className={`flex w-full items-center gap-2 rounded-lg px-2 py-2 text-left hover:bg-muted ${index === selectedIndex ? "bg-muted" : ""}`}
          onMouseEnter={() => setSelectedIndex(index)}
          onMouseDown={(event) => event.preventDefault()}
          onClick={() => selectItem(index)}
        >
          <Avatar
            avatarUrl={item.avatarUrl}
            initials={item.initials}
            className="grid size-7 shrink-0 place-items-center rounded-full bg-muted text-xs font-semibold"
          />
          <span className="flex-1 truncate">{item.name}</span>
        </button>
      ))}
    </div>
  );
});

MentionList.displayName = "MentionList";
