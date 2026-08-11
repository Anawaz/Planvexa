"use client";

import { useEffect } from "react";

/**
 * Every global shortcut action: the three dialogs ("search" | "quickAdd" | "help") plus two direct
 * navigation shortcuts ("myWork" | "inbox") that the consumer handles with router.push instead of the
 * shared dialog slot — see AuthenticatedAppLayout's useGlobalShortcuts call.
 */
export type ShortcutAction = "search" | "quickAdd" | "help" | "myWork" | "inbox";

export type ShortcutKeyEvent = {
  key: string;
  ctrlKey: boolean;
  metaKey: boolean;
  altKey: boolean;
  shiftKey?: boolean;
  /** True mid-IME composition — the keystroke belongs to the text being composed. */
  isComposing?: boolean;
};

export type ShortcutTarget = {
  tagName?: string;
  isContentEditable?: boolean;
} | null;

const typingTags = new Set(["INPUT", "TEXTAREA", "SELECT"]);

/** Whether the keystroke is being typed into a field rather than aimed at the app. */
export function isTypingTarget(target: ShortcutTarget) {
  if (!target) {
    return false;
  }

  return Boolean(target.isContentEditable) || typingTags.has(target.tagName ?? "");
}

/**
 * The whole shortcut table. Pure so it can be tested without a DOM.
 *
 * `blocked` covers both "typing in a field" and "a dialog already owns the keyboard": the single
 * letters are suppressed, while Ctrl/Cmd+K keeps working everywhere (including inside the palette's
 * own input, which is where it has always worked).
 */
export function shortcutFor(event: ShortcutKeyEvent, blocked: boolean): ShortcutAction | null {
  if (event.isComposing || event.altKey) {
    return null;
  }

  if (event.ctrlKey || event.metaKey) {
    return event.key.toLowerCase() === "k" ? "search" : null;
  }

  if (blocked) {
    return null;
  }

  // `?` needs Shift on most layouts, so Shift is not a blanket disqualifier here.
  switch (event.key) {
    case "n":
      return "quickAdd";
    case "m":
      return "myWork";
    case "i":
      return "inbox";
    case "/":
      // Shift+/ is the same physical chord as `?`; some layouts and automation report the
      // unshifted key, and that chord always means help rather than search.
      return event.shiftKey ? "help" : "search";
    case "?":
      return "help";
    default:
      return null;
  }
}

/**
 * Every global keyboard shortcut in the app, in one listener. Mounted once in the authenticated
 * shell layout; nothing else should add a window-level keydown handler for a shortcut.
 */
export function useGlobalShortcuts(onAction: (action: ShortcutAction) => void, blocked = false) {
  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      // ponytail: one DOM probe instead of a registry of open dialogs — every modal in the app
      // (task drawer, share, mobile nav, the shortcut dialogs themselves) sets aria-modal.
      const modalOpen = document.querySelector('[aria-modal="true"]') !== null;
      const action = shortcutFor(
        event,
        blocked || modalOpen || isTypingTarget(event.target as ShortcutTarget),
      );

      if (!action) {
        return;
      }

      event.preventDefault();
      onAction(action);
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [blocked, onAction]);
}
