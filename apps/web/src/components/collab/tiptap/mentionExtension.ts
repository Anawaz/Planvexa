// @mention node: extends Tiptap's Mention with markdown round-tripping (tiptap-markdown has no
// built-in support for custom nodes). Wire format is `@[Name](userId)` — a small custom markdown-it
// inline rule turns that into the HTML span Mention's own parseHTML already expects
// (span[data-type="mention"][data-id][data-label]), and the reverse on export. Kept deliberately
// plain-markdown-shaped (not raw HTML) so a mention still reads fine as text wherever a
// description/comment body is shown without parsing (AI summaries, notifications, search snippets).
import Mention from "@tiptap/extension-mention";
import type MarkdownIt from "markdown-it";

function escapeHtmlAttr(value: string): string {
  return value.replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

const MENTION_MARKDOWN_RE = /^@\[([^[\]]+)\]\(([^()]+)\)/;

function mentionMarkdownItPlugin(md: MarkdownIt) {
  md.inline.ruler.push("mention", (state, silent) => {
    const match = MENTION_MARKDOWN_RE.exec(state.src.slice(state.pos));
    if (!match) {
      return false;
    }
    if (!silent) {
      const token = state.push("mention", "", 0);
      token.meta = { name: match[1], userId: match[2] };
    }
    state.pos += match[0].length;
    return true;
  });
  md.renderer.rules.mention = (tokens, index) => {
    const { name, userId } = tokens[index].meta as { name: string; userId: string };
    return `<span data-type="mention" data-id="${escapeHtmlAttr(userId)}" data-label="${escapeHtmlAttr(name)}">@${escapeHtmlAttr(name)}</span>`;
  };
}

export const MentionExtension = Mention.extend({
  addStorage() {
    return {
      markdown: {
        serialize(state: { write: (text: string) => void }, node: { attrs: { id: string; label?: string | null } }) {
          state.write(`@[${node.attrs.label ?? node.attrs.id}](${node.attrs.id})`);
        },
        parse: {
          setup(markdownit: MarkdownIt) {
            markdownit.use(mentionMarkdownItPlugin);
          },
        },
      },
    };
  },
}).configure({
  HTMLAttributes: {
    class: "mx-0.5 rounded bg-primary/10 px-1 py-0.5 text-sm font-medium text-primary",
  },
});
