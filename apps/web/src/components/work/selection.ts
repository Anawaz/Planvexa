"use client";

import { useCallback, useMemo, useState } from "react";

export type TaskSelection = {
  selectedIds: string[];
  isSelected: (taskId: string) => boolean;
  toggle: (taskId: string) => void;
  /** Select-all / clear-all for the ids currently on screen. */
  setMany: (taskIds: string[], selected: boolean) => void;
  clear: () => void;
};

/** Row selection shared by the list and table views and the bulk action bar. */
export function useTaskSelection(): TaskSelection {
  const [selected, setSelected] = useState<Set<string>>(() => new Set());

  const toggle = useCallback((taskId: string) => {
    setSelected((current) => {
      const next = new Set(current);

      if (!next.delete(taskId)) {
        next.add(taskId);
      }

      return next;
    });
  }, []);

  const setMany = useCallback((taskIds: string[], isSelected: boolean) => {
    setSelected((current) => {
      const next = new Set(current);
      taskIds.forEach((taskId) => (isSelected ? next.add(taskId) : next.delete(taskId)));
      return next;
    });
  }, []);

  const clear = useCallback(() => setSelected(new Set()), []);

  return useMemo(
    () => ({
      selectedIds: [...selected],
      isSelected: (taskId: string) => selected.has(taskId),
      toggle,
      setMany,
      clear,
    }),
    [clear, selected, setMany, toggle],
  );
}
