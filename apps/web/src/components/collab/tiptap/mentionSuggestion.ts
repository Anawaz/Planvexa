import { ReactRenderer } from "@tiptap/react";
import type { SuggestionOptions } from "@tiptap/suggestion";
import { MentionList, type MentionListHandle, type MentionListItem } from "./MentionList";

/** Factory instead of a static config: `items()` needs the current member list, which is only known
 * once useMembers() resolves inside the editor component — `getMembers` is a live getter (backed by a
 * ref) rather than a snapshot, since Tiptap builds this extension config once at editor creation. */
export function createMentionSuggestion(
  getMembers: () => MentionListItem[],
): Omit<SuggestionOptions<MentionListItem>, "editor"> {
  return {
    // The default command spreads the selected item straight onto the mention node's attrs, which
    // only has `id`/`label` — without this, MentionListItem's `name` field is silently dropped and
    // the chip falls back to rendering the raw user id.
    command: ({ editor, range, props }) => {
      editor
        .chain()
        .focus()
        .insertContentAt(range, [
          { type: "mention", attrs: { id: props.id, label: props.name } },
          { type: "text", text: " " },
        ])
        .run();
    },
    items: ({ query }) => {
      const q = query.toLowerCase();
      return getMembers()
        .filter((member) => member.name.toLowerCase().includes(q))
        .slice(0, 8);
    },
    render: () => {
      let component: ReactRenderer<MentionListHandle, { items: MentionListItem[]; command: (item: MentionListItem) => void }>;
      let unmount: (() => void) | undefined;

      return {
        onStart: (props) => {
          component = new ReactRenderer(MentionList, {
            props: { items: props.items, command: (item: MentionListItem) => props.command(item) },
            editor: props.editor,
          });
          unmount = props.mount(component.element);
        },
        onUpdate: (props) => {
          component.updateProps({ items: props.items, command: (item: MentionListItem) => props.command(item) });
        },
        onKeyDown: (props) => {
          // Without this, Escape also bubbles to the task drawer's document-level focus trap
          // (useFocusTrap), which closes the whole drawer instead of just this popup.
          if (props.event.key === "Escape") {
            props.event.stopPropagation();
            unmount?.();
            return true;
          }
          const handled = component.ref?.onKeyDown(props) ?? false;
          if (handled) {
            props.event.stopPropagation();
          }
          return handled;
        },
        onExit: () => {
          unmount?.();
          component.destroy();
        },
      };
    },
  };
}
