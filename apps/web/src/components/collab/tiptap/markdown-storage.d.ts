// tiptap-markdown adds `storage.markdown` at runtime but doesn't augment Tiptap's Storage type for it
// (its own .d.ts never touches @tiptap/core) — this is the standard Tiptap module-augmentation pattern
// for extension storage.
import type { MarkdownStorage } from "tiptap-markdown";

declare module "@tiptap/core" {
  interface Storage {
    markdown: MarkdownStorage;
  }
}
