"use client";

import { forwardRef, useEffect, useRef, useState } from "react";
import { Stage, Layer, Rect, Ellipse, Line, Arrow, Text, Group, Image as KonvaImage, Transformer } from "react-konva";
import type Konva from "konva";
import type { HocuspocusProvider } from "@hocuspocus/provider";
import { Button } from "@/components/ui/Button";
import { whiteboardImageHref, uploadWhiteboardImage } from "@/lib/collab/client";
import { cn } from "@/lib/utils";
import { newShapeId, shapeCenter, type WhiteboardShape } from "./shapes";
import { useShapesMap } from "./useShapesMap";

type Tool = "select" | "rect" | "ellipse" | "line" | "arrow" | "sticky" | "text" | "connector" | "link";

const STAGE_WIDTH = 1400;
const STAGE_HEIGHT = 900;
const PALETTE = ["#fde68a", "#bfdbfe", "#bbf7d0", "#fbcfe8", "#e5e7eb"];

/** Konva.Image needs a loaded HTMLImageElement, not a URL — this fetches it through the same
 * cookie-authenticated proxy every other download in this app uses (see whiteboardImageHref). */
function useHtmlImage(src: string | undefined) {
  const [image, setImage] = useState<HTMLImageElement | undefined>(undefined);
  useEffect(() => {
    if (!src) return;
    const img = new window.Image();
    img.crossOrigin = "anonymous";
    img.src = src;
    img.onload = () => setImage(img);
    return () => setImage(undefined);
  }, [src]);
  return image;
}

type ImageShapeNodeProps = {
  shape: Extract<WhiteboardShape, { type: "image" }>;
  whiteboardId: string;
  draggable: boolean;
  onClick: () => void;
  onTap: () => void;
  onDragEnd: (event: Konva.KonvaEventObject<DragEvent>) => void;
  onTransformEnd: (event: Konva.KonvaEventObject<Event>) => void;
};

const ImageShapeNode = forwardRef<Konva.Image, ImageShapeNodeProps>(function ImageShapeNode(
  { shape, whiteboardId, ...rest },
  ref,
) {
  const image = useHtmlImage(whiteboardImageHref(whiteboardId, shape.imageId));
  return <KonvaImage ref={ref} image={image} x={shape.x} y={shape.y} width={shape.width} height={shape.height} rotation={shape.rotation} {...rest} />;
});

/**
 * The free-drawing/shapes/connectors/sticky-notes/text/images canvas, bound to the
 * whiteboard's Yjs room (see useShapesMap) so every edit is visible live to anyone else with this
 * whiteboard open — the exact same collaboration guarantee Documents' Lexical editor has, just over a
 * different content shape (canvas shapes instead of rich text).
 */
export function WhiteboardCanvas({
  whiteboardId,
  provider,
  canEdit,
  onOpenLink,
}: {
  whiteboardId: string;
  provider: HocuspocusProvider;
  canEdit: boolean;
  onOpenLink?: (resourceType: "task" | "document", resourceId: string) => void;
}) {
  const { shapes, upsert, remove } = useShapesMap(provider.document);
  const [tool, setTool] = useState<Tool>("select");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [connectorFromId, setConnectorFromId] = useState<string | null>(null);
  const stageRef = useRef<Konva.Stage>(null);
  const shapeRefs = useRef<Map<string, Konva.Node>>(new Map());
  const transformerRef = useRef<Konva.Transformer>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!transformerRef.current) return;
    const node = selectedId ? shapeRefs.current.get(selectedId) : undefined;
    transformerRef.current.nodes(node ? [node] : []);
    transformerRef.current.getLayer()?.batchDraw();
  }, [selectedId, shapes]);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (!canEdit || !selectedId) return;
      if (event.key === "Delete" || event.key === "Backspace") {
        const active = document.activeElement;
        if (active && (active.tagName === "INPUT" || active.tagName === "TEXTAREA")) return;
        remove(selectedId);
        setSelectedId(null);
      }
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [canEdit, selectedId, remove]);

  function placeAt(x: number, y: number) {
    const id = newShapeId();
    const fill = PALETTE[Math.floor(Math.random() * PALETTE.length)];
    let shape: WhiteboardShape;
    switch (tool) {
      case "rect":
        shape = { id, type: "rect", x, y, width: 160, height: 100, fill };
        break;
      case "ellipse":
        shape = { id, type: "ellipse", x, y, width: 140, height: 100, fill };
        break;
      case "line":
        shape = { id, type: "line", x, y, points: [0, 0, 140, 0], stroke: "#334155" };
        break;
      case "arrow":
        shape = { id, type: "arrow", x, y, points: [0, 0, 140, 0], stroke: "#334155" };
        break;
      case "sticky":
        shape = { id, type: "sticky", x, y, width: 160, height: 160, text: "Note", fill };
        break;
      case "text":
        shape = { id, type: "text", x, y, text: "Text", fontSize: 20 };
        break;
      default:
        return;
    }

    upsert(shape);
    setSelectedId(id);
    setTool("select");
  }

  function handleStageClick(event: Konva.KonvaEventObject<MouseEvent>) {
    if (!canEdit) return;
    const stage = stageRef.current;
    if (!stage) return;
    const pointer = stage.getRelativePointerPosition();
    if (!pointer) return;

    if (event.target === stage) {
      setSelectedId(null);
      if (tool !== "select" && tool !== "connector" && tool !== "link") {
        placeAt(pointer.x, pointer.y);
      }
    }
  }

  function handleShapeClick(shape: WhiteboardShape) {
    if (tool === "connector" && canEdit) {
      if (!connectorFromId) {
        setConnectorFromId(shape.id);
        return;
      }

      if (connectorFromId !== shape.id) {
        upsert({ id: newShapeId(), type: "connector", x: 0, y: 0, fromId: connectorFromId, toId: shape.id, stroke: "#0f172a" });
      }

      setConnectorFromId(null);
      setTool("select");
      return;
    }

    setSelectedId(shape.id);
    if (shape.type === "link" && onOpenLink) {
      onOpenLink(shape.resourceType, shape.resourceId);
    }
  }

  function editText(shape: Extract<WhiteboardShape, { type: "text" | "sticky" }>) {
    if (!canEdit) return;
    // ponytail: window.prompt for inline text edit — a Konva canvas can't host a native contentEditable
    // node, and overlaying an HTML textarea (the fuller Konva recipe) is real added complexity for a
    // first cut. Upgrade if sticky/text editing over a prompt() feels too clunky in practice.
    const next = window.prompt("Edit text", shape.text);
    if (next != null) upsert({ ...shape, text: next });
  }

  async function handleImageChosen(file: File) {
    const { imageId } = await uploadWhiteboardImage(whiteboardId, file);
    const id = newShapeId();
    upsert({ id, type: "image", x: 80, y: 80, width: 240, height: 180, imageId });
    setSelectedId(id);
  }

  function addLink() {
    const resourceType = window.prompt("Link to a task or document? Type 'task' or 'document'.", "task");
    if (resourceType !== "task" && resourceType !== "document") return;
    const resourceId = window.prompt(`${resourceType} id to link`);
    if (!resourceId) return;
    const label = window.prompt("Label", resourceType === "task" ? "Task" : "Document") ?? resourceType;
    const id = newShapeId();
    upsert({ id, type: "link", x: 100, y: 100, width: 180, height: 60, resourceType, resourceId, label });
    setSelectedId(id);
    setTool("select");
  }

  function exportPng() {
    const stage = stageRef.current;
    if (!stage) return;
    const uri = stage.toDataURL({ pixelRatio: 2 });
    const anchor = document.createElement("a");
    anchor.href = uri;
    anchor.download = `whiteboard-${whiteboardId}.png`;
    anchor.click();
  }

  const connectors = shapes.filter((s): s is Extract<WhiteboardShape, { type: "connector" }> => s.type === "connector");
  const byId = new Map(shapes.map((s) => [s.id, s]));

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center gap-1.5 rounded-[var(--radius)] border border-border bg-card p-2">
        {(
          [
            ["select", "Select"],
            ["rect", "Rectangle"],
            ["ellipse", "Circle"],
            ["line", "Line"],
            ["arrow", "Arrow"],
            ["sticky", "Sticky note"],
            ["text", "Text"],
            ["connector", "Connector"],
          ] as [Tool, string][]
        ).map(([value, label]) => (
          <Button
            key={value}
            type="button"
            size="sm"
            variant={tool === value ? "primary" : "outline"}
            disabled={!canEdit}
            onClick={() => {
              setTool(value);
              setConnectorFromId(null);
            }}
          >
            {label}
          </Button>
        ))}
        <Button type="button" size="sm" variant="outline" disabled={!canEdit} onClick={() => fileInputRef.current?.click()}>
          Image
        </Button>
        <input
          ref={fileInputRef}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={(event) => {
            const file = event.target.files?.[0];
            if (file) void handleImageChosen(file);
            event.target.value = "";
          }}
        />
        <Button type="button" size="sm" variant="outline" disabled={!canEdit} onClick={addLink}>
          Link task/doc
        </Button>
        <span className="mx-1 h-5 w-px bg-border" aria-hidden="true" />
        <Button type="button" size="sm" variant="outline" onClick={exportPng}>
          Export PNG
        </Button>
        {tool === "connector" ? (
          <span className="text-xs text-muted-foreground">
            {connectorFromId ? "Click the second shape…" : "Click the first shape…"}
          </span>
        ) : null}
      </div>

      <div className={cn("overflow-auto rounded-[var(--radius)] border border-border bg-white")}>
        <Stage
          ref={stageRef}
          width={STAGE_WIDTH}
          height={STAGE_HEIGHT}
          onMouseDown={handleStageClick}
          className={tool !== "select" && tool !== "connector" && tool !== "link" ? "cursor-crosshair" : undefined}
        >
          <Layer>
            {connectors.map((connector) => {
              const from = byId.get(connector.fromId);
              const to = byId.get(connector.toId);
              if (!from || !to) return null;
              const a = shapeCenter(from);
              const b = shapeCenter(to);
              return <Arrow key={connector.id} points={[a.x, a.y, b.x, b.y]} stroke={connector.stroke} fill={connector.stroke} strokeWidth={2} listening={false} />;
            })}

            {shapes.map((shape) => {
              if (shape.type === "connector") return null;
              const common = {
                key: shape.id,
                draggable: canEdit && tool === "select",
                onClick: () => handleShapeClick(shape),
                onTap: () => handleShapeClick(shape),
                ref: (node: Konva.Node | null) => {
                  if (node) shapeRefs.current.set(shape.id, node);
                  else shapeRefs.current.delete(shape.id);
                },
                onDragEnd: (event: Konva.KonvaEventObject<DragEvent>) => {
                  upsert({ ...shape, x: event.target.x(), y: event.target.y() });
                },
                onTransformEnd: (event: Konva.KonvaEventObject<Event>) => {
                  const node = event.target;
                  const scaleX = node.scaleX();
                  const scaleY = node.scaleY();
                  node.scaleX(1);
                  node.scaleY(1);
                  if ("width" in shape) {
                    upsert({
                      ...shape,
                      x: node.x(),
                      y: node.y(),
                      width: Math.max(20, shape.width * scaleX),
                      height: Math.max(20, shape.height * scaleY),
                      rotation: node.rotation(),
                    });
                  } else {
                    upsert({ ...shape, x: node.x(), y: node.y(), rotation: node.rotation() });
                  }
                },
              };

              switch (shape.type) {
                case "rect":
                  return <Rect {...common} x={shape.x} y={shape.y} width={shape.width} height={shape.height} fill={shape.fill} stroke="#334155" strokeWidth={1} cornerRadius={4} rotation={shape.rotation} />;
                case "ellipse":
                  return (
                    <Ellipse
                      {...common}
                      x={shape.x + shape.width / 2}
                      y={shape.y + shape.height / 2}
                      radiusX={shape.width / 2}
                      radiusY={shape.height / 2}
                      fill={shape.fill}
                      stroke="#334155"
                      strokeWidth={1}
                      rotation={shape.rotation}
                      onDragEnd={(event) => upsert({ ...shape, x: event.target.x() - shape.width / 2, y: event.target.y() - shape.height / 2 })}
                    />
                  );
                case "line":
                  return <Line {...common} x={shape.x} y={shape.y} points={shape.points} stroke={shape.stroke} strokeWidth={2} rotation={shape.rotation} />;
                case "arrow":
                  return <Arrow {...common} x={shape.x} y={shape.y} points={shape.points} stroke={shape.stroke} fill={shape.stroke} strokeWidth={2} rotation={shape.rotation} />;
                case "sticky":
                  return (
                    <Group {...common} x={shape.x} y={shape.y} rotation={shape.rotation} onDblClick={() => editText(shape)} onDblTap={() => editText(shape)}>
                      <Rect width={shape.width} height={shape.height} fill={shape.fill} shadowBlur={4} shadowOpacity={0.2} cornerRadius={2} />
                      <Text text={shape.text} width={shape.width} height={shape.height} padding={10} fontSize={16} align="left" verticalAlign="top" />
                    </Group>
                  );
                case "text":
                  return <Text {...common} x={shape.x} y={shape.y} text={shape.text} fontSize={shape.fontSize} fill="#0f172a" onDblClick={() => editText(shape)} onDblTap={() => editText(shape)} rotation={shape.rotation} />;
                case "image":
                  return (
                    <ImageShapeNode
                      key={shape.id}
                      shape={shape}
                      whiteboardId={whiteboardId}
                      draggable={common.draggable}
                      onClick={common.onClick}
                      onTap={common.onTap}
                      ref={common.ref}
                      onDragEnd={common.onDragEnd}
                      onTransformEnd={common.onTransformEnd}
                    />
                  );
                case "link":
                  return (
                    <Group {...common} x={shape.x} y={shape.y} rotation={shape.rotation}>
                      <Rect width={shape.width} height={shape.height} fill="#eef2ff" stroke="#6366f1" strokeWidth={1.5} cornerRadius={8} />
                      <Text
                        text={`🔗 ${shape.resourceType === "task" ? "Task" : "Document"}: ${shape.label}`}
                        width={shape.width}
                        height={shape.height}
                        padding={8}
                        fontSize={13}
                        fill="#4338ca"
                        align="center"
                        verticalAlign="middle"
                      />
                    </Group>
                  );
                default:
                  return null;
              }
            })}

            {canEdit ? <Transformer ref={transformerRef} rotateEnabled resizeEnabled /> : null}
          </Layer>
        </Stage>
      </div>
    </div>
  );
}
