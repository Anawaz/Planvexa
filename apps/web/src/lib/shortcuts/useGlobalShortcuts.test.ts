import { describe, expect, it } from "vitest";
import { isTypingTarget, shortcutFor, type ShortcutKeyEvent } from "./useGlobalShortcuts";

function key(overrides: Partial<ShortcutKeyEvent> & { key: string }): ShortcutKeyEvent {
  return { ctrlKey: false, metaKey: false, altKey: false, ...overrides };
}

describe("isTypingTarget", () => {
  it("treats form fields as typing", () => {
    expect(isTypingTarget({ tagName: "INPUT" })).toBe(true);
    expect(isTypingTarget({ tagName: "TEXTAREA" })).toBe(true);
    expect(isTypingTarget({ tagName: "SELECT" })).toBe(true);
  });

  it("treats contenteditable as typing", () => {
    expect(isTypingTarget({ tagName: "DIV", isContentEditable: true })).toBe(true);
  });

  it("lets plain elements and a missing target through", () => {
    expect(isTypingTarget({ tagName: "BUTTON" })).toBe(false);
    expect(isTypingTarget(null)).toBe(false);
  });
});

describe("shortcutFor", () => {
  it("maps the single-key shortcuts", () => {
    expect(shortcutFor(key({ key: "n" }), false)).toBe("quickAdd");
    expect(shortcutFor(key({ key: "/" }), false)).toBe("search");
    expect(shortcutFor(key({ key: "?" }), false)).toBe("help");
    expect(shortcutFor(key({ key: "m" }), false)).toBe("myWork");
    expect(shortcutFor(key({ key: "i" }), false)).toBe("inbox");
  });

  it("blocks the navigation shortcuts the same as the others while typing", () => {
    expect(shortcutFor(key({ key: "m" }), true)).toBeNull();
    expect(shortcutFor(key({ key: "i" }), true)).toBeNull();
  });

  it("treats Shift+/ as help, since it is the same chord as ?", () => {
    expect(shortcutFor(key({ key: "/", shiftKey: true }), false)).toBe("help");
  });

  it("maps Ctrl+K and Cmd+K to the palette", () => {
    expect(shortcutFor(key({ key: "k", ctrlKey: true }), false)).toBe("search");
    expect(shortcutFor(key({ key: "K", metaKey: true }), false)).toBe("search");
  });

  it("keeps Ctrl+K working while blocked, unlike the single keys", () => {
    expect(shortcutFor(key({ key: "k", ctrlKey: true }), true)).toBe("search");
    expect(shortcutFor(key({ key: "n" }), true)).toBeNull();
    expect(shortcutFor(key({ key: "/" }), true)).toBeNull();
  });

  it("ignores other modified keys", () => {
    expect(shortcutFor(key({ key: "n", ctrlKey: true }), false)).toBeNull();
    expect(shortcutFor(key({ key: "n", metaKey: true }), false)).toBeNull();
    expect(shortcutFor(key({ key: "n", altKey: true }), false)).toBeNull();
  });

  it("ignores keystrokes that belong to an IME composition", () => {
    expect(shortcutFor(key({ key: "n", isComposing: true }), false)).toBeNull();
    expect(shortcutFor(key({ key: "k", ctrlKey: true, isComposing: true }), false)).toBeNull();
  });

  it("ignores unmapped keys", () => {
    expect(shortcutFor(key({ key: "x" }), false)).toBeNull();
    expect(shortcutFor(key({ key: "Escape" }), false)).toBeNull();
  });
});
