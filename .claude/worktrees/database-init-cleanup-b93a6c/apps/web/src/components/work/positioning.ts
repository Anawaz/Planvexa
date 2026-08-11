const step = 1024;

/** Midpoint between two neighbours; mirrors the backend Positioning helper. */
// ponytail: midpoint positions; rebalance on collision.
export function midpoint(before?: number, after?: number) {
  if (before === undefined) {
    return after === undefined ? step : after - step;
  }

  return after === undefined ? before + step : (before + after) / 2;
}

/**
 * Position for dropping `activeId` at `overId` inside one board column. `column` must be sorted by
 * position and contain the dragged card when it started there; `overId` undefined means append.
 */
export function dropPosition(
  column: Array<{ id: string; position: number }>,
  activeId: string,
  overId?: string,
) {
  const activeIndex = column.findIndex((task) => task.id === activeId);
  const overIndex = overId ? column.findIndex((task) => task.id === overId) : -1;
  // Dragging a card downwards past its neighbour means dropping after it, not before it.
  const movingDown = activeIndex >= 0 && overIndex > activeIndex;
  const neighbours = column.filter((task) => task.id !== activeId);
  const insertAt =
    overIndex >= 0
      ? neighbours.findIndex((task) => task.id === overId) + (movingDown ? 1 : 0)
      : neighbours.length;

  return midpoint(neighbours[insertAt - 1]?.position, neighbours[insertAt]?.position);
}
