"use client";

import {
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type ColumnSizingState,
  type SortingState,
  type VisibilityState,
} from "@tanstack/react-table";
import { useVirtualizer } from "@tanstack/react-virtual";
import { useEffect, useMemo, useRef, useState } from "react";
import { useMemberDirectory } from "@/lib/members";
import type { ConditionalFormattingRule, StatusDefinition, Task } from "@/lib/work/types";
import { cn } from "@/lib/utils";
import {
  dueDateClassName,
  findStatus,
  firstMatchingRule,
  formatDate,
  priorityClassName,
  statusBadgeStyle,
} from "./helpers";
import type { TaskSelection } from "./selection";

type TableViewProps = {
  tasks: Task[];
  statuses: StatusDefinition[];
  /** The list has no tasks at all, as opposed to the filters hiding every row. */
  listIsEmpty?: boolean;
  selection: TaskSelection;
  /**  . uc(")if field X matches condition Y, apply style Z" -- badge color or row highlight. */
  formattingRules?: ConditionalFormattingRule[];
  onOpenTask: (taskId: string) => void;
};

const priorityWeight: Record<Task["priority"], number> = {
  None: 0,
  Low: 1,
  Normal: 2,
  High: 3,
  Urgent: 4,
};

// Column widths persist per browser (not per saved view -- a lighter "just remember what I
// last dragged" store, consistent with the view-mode/space persistence used elsewhere in this app).
const columnSizingStorageKey = "planvexa-work-table-column-sizing";

function loadStoredColumnSizing(): ColumnSizingState {
  if (typeof window === "undefined") {
    return {};
  }

  try {
    const raw = window.localStorage.getItem(columnSizingStorageKey);
    return raw ? (JSON.parse(raw) as ColumnSizingState) : {};
  } catch {
    return {};
  }
}

export function TableView({
  tasks,
  statuses,
  listIsEmpty = false,
  selection,
  formattingRules = [],
  onOpenTask,
}: TableViewProps) {
  const scrollParentRef = useRef<HTMLDivElement>(null);
  const { getLabel, getInitials } = useMemberDirectory();
  const [sorting, setSorting] = useState<SortingState>([
    { id: "dueDate", desc: false },
  ]);
  const [columnVisibility, setColumnVisibility] = useState<VisibilityState>({});
  const [columnSizing, setColumnSizing] = useState<ColumnSizingState>(loadStoredColumnSizing);

  useEffect(() => {
    window.localStorage.setItem(columnSizingStorageKey, JSON.stringify(columnSizing));
  }, [columnSizing]);

  const columns = useMemo<ColumnDef<Task>[]>(
    () => [
      {
        id: "select",
        header: "Select",
        enableSorting: false,
        enableHiding: false,
        enableResizing: false,
        size: 56,
        cell: ({ row }) => (
          <input
            type="checkbox"
            className="size-4 rounded border-border accent-[var(--primary)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            checked={selection.isSelected(row.original.id)}
            aria-label={`Select ${row.original.title}`}
            onChange={() => selection.toggle(row.original.id)}
          />
        ),
      },
      {
        accessorKey: "title",
        header: "Title",
        enableHiding: false,
        size: 320,
        minSize: 160,
        cell: ({ row }) => (
          <button
            type="button"
            className="max-w-full truncate text-left font-medium hover:text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            onClick={() => onOpenTask(row.original.id)}
          >
            {row.original.title}
          </button>
        ),
      },
      {
        id: "status",
        accessorFn: (task) => findStatus(statuses, task.statusId)?.name ?? "",
        header: "Status",
        size: 144,
        minSize: 96,
        cell: ({ row }) => {
          const status = findStatus(statuses, row.original.statusId);

          return (
            <span
              className="inline-flex rounded-full border px-2 py-0.5 text-xs font-medium"
              style={statusBadgeStyle(status)}
            >
              {status?.name ?? "Unknown"}
            </span>
          );
        },
      },
      {
        accessorKey: "priority",
        header: "Priority",
        size: 128,
        minSize: 96,
        sortingFn: (left, right) =>
          priorityWeight[left.original.priority] - priorityWeight[right.original.priority],
        cell: ({ row }) => (
          <span className={priorityClassName(row.original.priority)}>
            {row.original.priority}
          </span>
        ),
      },
      {
        id: "assignees",
        accessorFn: (task) => task.assigneeUserIds.map(getLabel).join(", "),
        header: "Assignees",
        size: 144,
        minSize: 96,
        cell: ({ row }) => (
          <div className="flex flex-wrap gap-1">
            {row.original.assigneeUserIds.map((userId) => (
              <span
                key={userId}
                title={getLabel(userId)}
                className="grid size-7 place-items-center rounded-full border border-border bg-background text-[0.7rem] font-semibold text-muted-foreground"
              >
                {getInitials(userId)}
              </span>
            ))}
          </div>
        ),
      },
      {
        accessorKey: "dueDate",
        header: "Due date",
        size: 144,
        minSize: 96,
        cell: ({ row }) => (
          <span className={dueDateClassName(row.original.dueDate, row.original.isCompleted)}>
            {formatDate(row.original.dueDate)}
          </span>
        ),
      },
    ],
    [getInitials, getLabel, onOpenTask, selection, statuses],
  );

  // eslint-disable-next-line react-hooks/incompatible-library
  const table = useReactTable({
    data: tasks,
    columns,
    state: { sorting, columnVisibility, columnSizing },
    columnResizeMode: "onChange",
    onSortingChange: setSorting,
    onColumnVisibilityChange: setColumnVisibility,
    onColumnSizingChange: setColumnSizing,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
  });

  const rows = table.getRowModel().rows;
  const rowVirtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollParentRef.current,
    estimateSize: () => 60,
    overscan: 8,
  });
  const visibleColumns = table.getVisibleLeafColumns();
  // Column widths now come from TanStack Table's own sizing state (persisted above) instead of
  // the fixed minmax() strings this used to hardcode.
  const gridTemplateColumns = visibleColumns.map((column) => `${column.getSize()}px`).join(" ");
  const allSelected = tasks.length > 0 && tasks.every((task) => selection.isSelected(task.id));

  return (
    <section
      aria-labelledby="task-table-heading"
      className="rounded-[var(--radius)] border border-border bg-card shadow-sm"
    >
      <div className="flex flex-col gap-3 border-b border-border p-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h2 id="task-table-heading" className="text-sm font-semibold">
            Table view
          </h2>
          <p className="text-xs text-muted-foreground">
            Sort columns and hide fields without changing the shared task source.
          </p>
        </div>
        <fieldset className="flex flex-wrap items-center gap-3 text-xs">
          <legend className="sr-only">Configure visible columns</legend>
          <label className="inline-flex items-center gap-1.5 font-medium">
            <input
              type="checkbox"
              className="size-3.5 accent-[var(--primary)]"
              checked={allSelected}
              disabled={tasks.length === 0}
              onChange={(event) =>
                selection.setMany(
                  tasks.map((task) => task.id),
                  event.currentTarget.checked,
                )
              }
            />
            <span>Select all</span>
          </label>
          {table.getAllLeafColumns().filter((column) => column.getCanHide()).map((column) => (
            <label key={column.id} className="inline-flex items-center gap-1.5">
              <input
                type="checkbox"
                className="size-3.5 accent-[var(--primary)]"
                checked={column.getIsVisible()}
                disabled={!column.getCanHide()}
                onChange={column.getToggleVisibilityHandler()}
              />
              <span>{String(column.columnDef.header)}</span>
            </label>
          ))}
        </fieldset>
      </div>
      <div role="table" aria-rowcount={rows.length} className="text-sm">
        <div role="rowgroup" className="border-b border-border bg-muted/60">
          {table.getHeaderGroups().map((headerGroup) => (
            <div
              key={headerGroup.id}
              role="row"
              className="grid min-w-max"
              style={{ gridTemplateColumns }}
            >
              {headerGroup.headers.map((header) => (
                <div key={header.id} role="columnheader" className="relative px-4 py-3">
                  {header.column.getCanSort() ? (
                    <button
                      type="button"
                      className="inline-flex items-center gap-1 rounded text-left font-medium text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                      onClick={header.column.getToggleSortingHandler()}
                    >
                      {flexRender(header.column.columnDef.header, header.getContext())}
                      <span aria-hidden="true">
                        {{
                          asc: "↑",
                          desc: "↓",
                        }[header.column.getIsSorted() as string] ?? "↕"}
                      </span>
                    </button>
                  ) : (
                    flexRender(header.column.columnDef.header, header.getContext())
                  )}
                  {header.column.getCanResize() ? (
                    <div
                      role="separator"
                      aria-orientation="vertical"
                      aria-label={`Resize ${String(header.column.columnDef.header)} column`}
                      onMouseDown={header.getResizeHandler()}
                      onTouchStart={header.getResizeHandler()}
                      onDoubleClick={() => header.column.resetSize()}
                      className={cn(
                        "absolute right-0 top-0 h-full w-1.5 cursor-col-resize touch-none select-none hover:bg-primary/40",
                        header.column.getIsResizing() && "bg-primary",
                      )}
                    />
                  ) : null}
                </div>
              ))}
            </div>
          ))}
        </div>
        {rows.length === 0 ? (
          // The table has no composer of its own; the page's zero-state above carries the CTA.
          <p role="status" className="px-4 py-8 text-sm text-muted-foreground">
            {listIsEmpty
              ? "No tasks yet — use “New task” above to add the first one."
              : "No tasks match the current filters."}
          </p>
        ) : null}
        <div
          ref={scrollParentRef}
          role="rowgroup"
          className="max-h-[34rem] overflow-auto"
          tabIndex={0}
          aria-label="Virtualized task rows"
        >
          <div
            className="relative min-w-max"
            style={{ height: `${rowVirtualizer.getTotalSize()}px` }}
          >
            {rowVirtualizer.getVirtualItems().map((virtualRow) => {
              const row = rows[virtualRow.index];
              // First matching conditional-formatting rule wins (declaration order).
              const rule = firstMatchingRule(row.original, formattingRules);

              return (
                <div
                  key={row.id}
                  role="row"
                  aria-rowindex={virtualRow.index + 1}
                  className={cn(
                    "absolute left-0 grid w-full items-center border-b border-border bg-card",
                    row.original.isCompleted && "opacity-70",
                  )}
                  style={{
                    gridTemplateColumns,
                    transform: `translateY(${virtualRow.start}px)`,
                    ...(rule?.style === "row"
                      ? { backgroundColor: `${rule.color}1a`, borderLeft: `3px solid ${rule.color}` }
                      : {}),
                  }}
                >
                  {row.getVisibleCells().map((cell) => (
                    <div key={cell.id} role="cell" className="min-w-0 px-4 py-3">
                      {cell.column.id === "title" && rule?.style === "badge" ? (
                        <span
                          className="mr-1.5 inline-block size-2 rounded-full align-middle"
                          style={{ backgroundColor: rule.color }}
                          aria-hidden="true"
                          title="Matches a conditional formatting rule"
                        />
                      ) : null}
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </div>
                  ))}
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </section>
  );
}
