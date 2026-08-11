"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import type * as Y from "yjs";
import type { WhiteboardShape } from "./shapes";

/** Binds a Yjs `Y.Map<WhiteboardShape>` ("shapes") to React state, observing remote changes from every
 * other participant in the room (shapes/connectors/sticky-notes/text/images are Yjs CRDT
 * state, same "collaboratively editable" shape as Documents' Lexical content). */
export function useShapesMap(ydoc: Y.Doc) {
  const yMap = useMemo(() => ydoc.getMap<WhiteboardShape>("shapes"), [ydoc]);
  const [shapes, setShapes] = useState<WhiteboardShape[]>(() => Array.from(yMap.values()));

  useEffect(() => {
    const sync = () => setShapes(Array.from(yMap.values()));
    yMap.observe(sync);
    sync();
    return () => yMap.unobserve(sync);
  }, [yMap]);

  const upsert = useCallback((shape: WhiteboardShape) => yMap.set(shape.id, shape), [yMap]);

  const remove = useCallback(
    (id: string) => {
      yMap.delete(id);
      // Cascade: a connector pointing at a deleted shape has nowhere to attach.
      for (const shape of yMap.values()) {
        if (shape.type === "connector" && (shape.fromId === id || shape.toId === id)) {
          yMap.delete(shape.id);
        }
      }
    },
    [yMap],
  );

  return { shapes, upsert, remove, byId: useMemo(() => new Map(shapes.map((s) => [s.id, s])), [shapes]) };
}
