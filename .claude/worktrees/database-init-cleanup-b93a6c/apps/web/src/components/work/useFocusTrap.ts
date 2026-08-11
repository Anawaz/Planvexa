"use client";

import { useEffect, useRef, type RefObject } from "react";

const focusableSelector =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

type FocusTrapOptions = {
  open: boolean;
  containerRef: RefObject<HTMLElement | null>;
  onClose: () => void;
  /** A nested dialog owns the keyboard while this is true (Escape/Tab pass straight through). */
  paused?: boolean;
  /** Focused instead of the container itself once the dialog opens. */
  initialFocusRef?: RefObject<HTMLElement | null>;
};

/**
 * Modal keyboard plumbing shared by the task drawer and the quick-add dialog: focus in on open,
 * Escape to close, Tab cycles inside the container, focus back to the opener on unmount.
 */
export function useFocusTrap({
  open,
  containerRef,
  onClose,
  paused = false,
  initialFocusRef,
}: FocusTrapOptions) {
  const pausedRef = useRef(paused);
  const previousFocusRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    pausedRef.current = paused;
  }, [paused]);

  useEffect(() => {
    if (!open) {
      return;
    }

    previousFocusRef.current = document.activeElement as HTMLElement | null;
    window.requestAnimationFrame(() =>
      (initialFocusRef?.current ?? containerRef.current)?.focus(),
    );

    function handleKeyDown(event: KeyboardEvent) {
      if (pausedRef.current) {
        return;
      }

      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
        return;
      }

      if (event.key !== "Tab") {
        return;
      }

      const container = containerRef.current;
      const focusable = Array.from(
        container?.querySelectorAll<HTMLElement>(focusableSelector) ?? [],
      ).filter((element) => !element.hasAttribute("disabled"));

      if (focusable.length === 0) {
        event.preventDefault();
        container?.focus();
        return;
      }

      const first = focusable[0];
      const last = focusable[focusable.length - 1];

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      previousFocusRef.current?.focus();
    };
  }, [containerRef, initialFocusRef, onClose, open]);
}
