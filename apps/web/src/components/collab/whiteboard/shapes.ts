/**  . uc(t)he whiteboard's shape data model, stored in a Yjs Y.Map ("shapes", keyed by shape
 * id) synced through apps/collaboration's Hocuspocus room — see WhiteboardCanvas.tsx.
 *
 * ponytail: each shape is one Y.Map ENTRY (a whole plain object per key), not a nested Y.Map per
 * attribute — last-write-wins per shape rather than per-property CRDT merge. Ceiling: two people editing
 * different attributes of the SAME shape at the exact same instant can clobber each other's change (rare
 * in practice — whiteboard edits are drag/resize/retype, not simultaneous multi-attribute edits). Upgrade
 * to a nested Y.Map per shape if that proves to matter. */
export type ShapeType = "rect" | "ellipse" | "line" | "arrow" | "sticky" | "text" | "image" | "connector" | "link";

export type BaseShape = {
  id: string;
  type: ShapeType;
  x: number;
  y: number;
  rotation?: number;
};

export type RectShape = BaseShape & { type: "rect"; width: number; height: number; fill: string };
export type EllipseShape = BaseShape & { type: "ellipse"; width: number; height: number; fill: string };
export type LineShape = BaseShape & { type: "line"; points: number[]; stroke: string };
export type ArrowShape = BaseShape & { type: "arrow"; points: number[]; stroke: string };
export type StickyShape = BaseShape & { type: "sticky"; width: number; height: number; text: string; fill: string };
export type TextShape = BaseShape & { type: "text"; text: string; fontSize: number };
export type ImageShape = BaseShape & { type: "image"; width: number; height: number; imageId: string };
/** A line between two other shapes, tracked by id — endpoints are recomputed live from the referenced
 * shapes' current position/size every render, so it "stays attached" as they move. */
export type ConnectorShape = BaseShape & { type: "connector"; fromId: string; toId: string; stroke: string };
/** A linkable node referencing a Task or Document by id (the "task/document link" element,
 * mirroring the Lexical task-reference node for whiteboards). */
export type LinkShape = BaseShape & {
  type: "link";
  width: number;
  height: number;
  resourceType: "task" | "document";
  resourceId: string;
  label: string;
};

export type WhiteboardShape =
  | RectShape
  | EllipseShape
  | LineShape
  | ArrowShape
  | StickyShape
  | TextShape
  | ImageShape
  | ConnectorShape
  | LinkShape;

export function shapeCenter(shape: WhiteboardShape): { x: number; y: number } {
  switch (shape.type) {
    case "rect":
    case "ellipse":
    case "sticky":
    case "image":
    case "link":
      return { x: shape.x + shape.width / 2, y: shape.y + shape.height / 2 };
    case "text":
      return { x: shape.x, y: shape.y };
    case "line":
    case "arrow":
      return { x: shape.x, y: shape.y };
    case "connector":
      return { x: shape.x, y: shape.y };
  }
}

export function newShapeId() {
  return `s-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}
