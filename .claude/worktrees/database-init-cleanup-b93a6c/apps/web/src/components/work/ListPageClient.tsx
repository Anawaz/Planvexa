"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { useMembers } from "@/lib/members";
import {
  createView,
  getList,
  listFavorites,
  listStatusSchemes,
  listTags,
  listTasks,
  listViews,
  setListDefaultView,
  toggleFavorite,
  updateView,
} from "@/lib/work/client";
import { useRecordRecentView } from "@/lib/recent/useRecordRecentView";
import { useWorkMutation } from "@/lib/work/mutations";
import { workKeys } from "@/lib/work/queries";
import type { ConditionalFormattingRule, FilterGroup, ListTasksFilters, SavedView, SavedViewType } from "@/lib/work/types";
import { BoardView } from "./BoardView";
import { BulkActionBar } from "./BulkActionBar";
import { ConditionalFormattingEditor } from "./ConditionalFormattingEditor";
import { FilterBuilder } from "./FilterBuilder";
import { ListView } from "./ListView";
import { QuickAddTask } from "./QuickAddTask";
import { useTaskSelection } from "./selection";
import { TableView } from "./TableView";
import { TaskDetailPanel } from "./TaskDetailPanel";

const EMPTY_FILTER_GROUP: FilterGroup = { logic: "And", conditions: [], groups: [] };

/**  . uc(t)his view's own config -- filter tree + conditional-formatting rules -- round-tripped
 * through SavedView.configJson (already an opaque JSON blob, so no schema change). */
type ViewConfig = { filterGroup?: FilterGroup; formattingRules?: ConditionalFormattingRule[] };

function parseViewConfig(view: SavedView | undefined): ViewConfig {
  if (!view?.configJson) {
    return {};
  }

  try {
    return JSON.parse(view.configJson) as ViewConfig;
  } catch {
    return {};
  }
}

type ViewMode = "list" | "table" | "board";

const viewModes: ViewMode[] = ["list", "table", "board"];
const storageKey = "planvexa-work-list-view";
const fieldClassName =
  "h-9 rounded-lg border border-border bg-background px-2 text-sm font-normal text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";
const sortOptions: Array<[NonNullable<ListTasksFilters["sort"]>, string]> = [
  ["position", "Manual"],
  ["title", "Title"],
  ["priority", "Priority"],
  ["dueDate", "Due date"],
];

function parseViewMode(value: string | null): ViewMode | null {
  return value === "list" || value === "table" || value === "board" ? value : null;
}

/** SavedViewType values this frontend has a renderer for; Calendar/Timeline/Gantt are not built yet. */
function savedViewTypeToMode(viewType: SavedViewType): ViewMode | null {
  return viewType === "List" ? "list" : viewType === "Table" ? "table" : viewType === "Board" ? "board" : null;
}

function modeToSavedViewType(mode: ViewMode): SavedViewType {
  return mode === "list" ? "List" : mode === "table" ? "Table" : "Board";
}

export function ListPageClient({ listId }: { listId: string }) {
  useRecordRecentView("list", listId);
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const [storedViewMode] = useState<ViewMode>(() => {
    if (typeof window === "undefined") {
      return "list";
    }

    return (
      parseViewMode(new URLSearchParams(window.location.search).get("view")) ??
      parseViewMode(window.localStorage.getItem(storageKey)) ??
      "list"
    );
  });
  // Only auto-apply the list's recorded default view when the caller has not already picked
  // one explicitly (a URL/localStorage value always wins — this only fills in the very first visit).
  const hadExplicitViewChoice = useRef(
    typeof window !== "undefined" &&
      Boolean(
        parseViewMode(new URLSearchParams(window.location.search).get("view")) ??
          parseViewMode(window.localStorage.getItem(storageKey)),
      ),
  ).current;
  const appliedDefaultViewRef = useRef(false);
  const [quickAddOpen, setQuickAddOpen] = useState(false);
  const [filters, setFilters] = useState<ListTasksFilters>({ sort: "position" });
  const [search, setSearch] = useState("");
  const selection = useTaskSelection();
  const listQuery = useQuery({
    queryKey: workKeys.list(listId),
    queryFn: () => getList(listId),
  });
  const statusQuery = useQuery({
    queryKey: workKeys.statusSchemes(),
    queryFn: listStatusSchemes,
  });
  const tasksQuery = useQuery({
    queryKey: workKeys.tasks(listId, filters),
    queryFn: () => listTasks(listId, filters),
    // Changing a filter is a new query key; keep the old rows so the page does not flash its loader.
    placeholderData: keepPreviousData,
  });
  const tagsQuery = useQuery({ queryKey: workKeys.tags(), queryFn: listTags });
  const membersQuery = useMembers();
  const viewsQuery = useQuery({ queryKey: workKeys.views(), queryFn: listViews });
  const favoritesQuery = useQuery({ queryKey: workKeys.favorites(), queryFn: listFavorites });

  // Nested filter groups + conditional-formatting rules, hydrated from (and saved back
  // to) the SavedView matching this list's current mode -- see ViewConfig/parseViewConfig above.
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [filterGroup, setFilterGroupState] = useState<FilterGroup>(EMPTY_FILTER_GROUP);
  const [formattingRules, setFormattingRules] = useState<ConditionalFormattingRule[]>([]);
  const hydratedViewIdRef = useRef<string | null>(null);
  // ponytail: title search stays out of the query key so typing does not refetch per keystroke.
  const tasks = useMemo(() => {
    const term = search.trim().toLowerCase();
    const loaded = tasksQuery.data ?? [];

    return term ? loaded.filter((task) => task.title.toLowerCase().includes(term)) : loaded;
  }, [search, tasksQuery.data]);
  const completedCount = tasks.filter((task) => task.isCompleted).length;
  const statusScheme =
    statusQuery.data?.find((scheme) => scheme.id === listQuery.data?.statusSchemeId) ??
    statusQuery.data?.[0] ??
    null;
  const statuses = statusScheme?.statuses ?? [];
  const viewMode = parseViewMode(searchParams.get("view")) ?? storedViewMode;
  // The drawer reads its task from the URL so search results (and back/forward) can open one.
  const selectedTaskId = searchParams.get("task");
  // Empty *list*, not empty *result set*: the filters above have their own per-group messages.
  const listIsEmpty = (tasksQuery.data ?? []).length === 0;
  const currentView = (viewsQuery.data ?? []).find(
    (view) => view.scopeType === "List" && view.scopeId === listId && view.viewType === modeToSavedViewType(viewMode),
  );
  const isCurrentViewFavorited = Boolean(
    currentView && (favoritesQuery.data ?? []).some((favorite) => favorite.resourceType === "SavedView" && favorite.resourceId === currentView.id),
  );

  // Hydrate the filter/formatting state from the current view's config, once per view id (further
  // edits stay local until "Save to this view" is used, so the panel does not fight the network).
  useEffect(() => {
    if (!currentView || hydratedViewIdRef.current === currentView.id) {
      return;
    }

    hydratedViewIdRef.current = currentView.id;
    const config = parseViewConfig(currentView);
    setFilterGroupState(config.filterGroup ?? EMPTY_FILTER_GROUP);
    setFormattingRules(config.formattingRules ?? []);
  }, [currentView]);

  function setFilterGroup(next: FilterGroup) {
    setFilterGroupState(next);
    const hasConditions = (next.conditions?.length ?? 0) > 0 || (next.groups?.length ?? 0) > 0;
    setFilters((current) => ({ ...current, filterGroup: hasConditions ? next : undefined }));
  }

  useEffect(() => {
    const urlView = parseViewMode(searchParams.get("view"));

    if (urlView) {
      window.localStorage.setItem(storageKey, urlView);
      return;
    }

    const params = new URLSearchParams(searchParams.toString());
    params.set("view", viewMode);
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  }, [pathname, router, searchParams, viewMode]);

  // Navigating to this List with no explicit view choice yet opens its recorded default view
  // (falls back to List when the default points at a view type this frontend cannot render, or none is set).
  useEffect(() => {
    if (hadExplicitViewChoice || appliedDefaultViewRef.current) {
      return;
    }

    const defaultViewId = listQuery.data?.defaultViewId;
    if (!defaultViewId || !viewsQuery.data) {
      return;
    }

    const defaultView = viewsQuery.data.find((view) => view.id === defaultViewId);
    const mode = defaultView ? savedViewTypeToMode(defaultView.viewType) : null;
    if (mode) {
      appliedDefaultViewRef.current = true;
      changeViewMode(mode);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- changeViewMode is stable enough here; including it would re-run every render.
  }, [hadExplicitViewChoice, listQuery.data?.defaultViewId, viewsQuery.data]);

  const setDefaultView = useWorkMutation(async (mode: ViewMode) => {
    const viewType = modeToSavedViewType(mode);
    const existing = (viewsQuery.data ?? []).find(
      (view) => view.scopeType === "List" && view.scopeId === listId && view.viewType === viewType,
    );
    const view = existing ?? (await createView({ viewType, scopeType: "List", scopeId: listId, name: `${mode} view` }));
    return setListDefaultView(listId, view.id);
  });

  /** Finds this List's SavedView for the given mode, creating it (with the current in-memory config
   * baked in) if it does not exist yet -- shared by save/favourite/template below. */
  async function findOrCreateCurrentView(mode: ViewMode) {
    const viewType = modeToSavedViewType(mode);
    const existing = (viewsQuery.data ?? []).find(
      (view) => view.scopeType === "List" && view.scopeId === listId && view.viewType === viewType,
    );
    if (existing) {
      return existing;
    }

    const config: ViewConfig = { filterGroup, formattingRules };
    return createView({
      viewType,
      scopeType: "List",
      scopeId: listId,
      name: `${mode} view`,
      config: JSON.stringify(config),
    });
  }

  // Persists the current filter tree + formatting rules onto this List's SavedView for the
  // active mode (creating it first if this is the first time anyone has customized this view).
  const saveViewConfig = useWorkMutation(async () => {
    const view = await findOrCreateCurrentView(viewMode);
    const config: ViewConfig = { filterGroup, formattingRules };
    return updateView(view.id, { config: JSON.stringify(config) });
  });

  // Reuses the generic favourites mechanism (WorkFavorite.resourceType is
  // free-form) with "SavedView" as the resource type -- no backend change was needed for this.
  const toggleViewFavorite = useWorkMutation(async () => {
    const view = await findOrCreateCurrentView(viewMode);
    return toggleFavorite("SavedView", view.id);
  });

  // The lightest version that fits -- SavedView already stores a name + full
  // config, so "save as template" is just creating another named SavedView with the same config
  // (the WorkTemplate snapshots structure/status-schemes, a different concept from a view preset).
  const saveAsTemplate = useWorkMutation(async () => {
    const name = window.prompt("Name this view template:", `${viewMode} preset`);
    if (!name) {
      return null;
    }

    const config: ViewConfig = { filterGroup, formattingRules };
    return createView({
      viewType: modeToSavedViewType(viewMode),
      scopeType: "List",
      scopeId: listId,
      name,
      config: JSON.stringify(config),
    });
  });

  function changeViewMode(nextView: ViewMode) {
    const params = new URLSearchParams(searchParams.toString());
    params.set("view", nextView);
    window.localStorage.setItem(storageKey, nextView);
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  }

  /**
   * Opening pushes a history entry (Back closes the drawer); closing replaces it, so Back from a
   * closed drawer does not reopen the task the user just dismissed.
   */
  function openTask(taskId: string | null) {
    const params = new URLSearchParams(searchParams.toString());

    if (taskId) {
      params.set("task", taskId);
      router.push(`${pathname}?${params.toString()}`, { scroll: false });
      return;
    }

    params.delete("task");
    router.replace(`${pathname}?${params.toString()}`, { scroll: false });
  }

  if (listQuery.isLoading || statusQuery.isLoading || tasksQuery.isLoading) {
    return (
      <section className="rounded-[var(--radius)] border border-border bg-card p-6 text-sm text-muted-foreground">
        Loading work management view…
      </section>
    );
  }

  if (listQuery.isError || statusQuery.isError || tasksQuery.isError || !listQuery.data) {
    return (
      <section className="rounded-[var(--radius)] border border-border bg-card p-6">
        <h1 className="text-xl font-semibold">List unavailable</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          This task list could not be loaded. Refresh the page, or check that you still have access
          to it.
        </p>
      </section>
    );
  }

  return (
    <section aria-labelledby="list-title" className="space-y-6">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <p className="text-sm font-medium text-primary">Core Work Management</p>
          <h1 id="list-title" className="mt-2 text-3xl font-semibold tracking-tight">
            {listQuery.data.name}
          </h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
            {tasks.length} tasks · {completedCount} completed · One shared query powers
            List, Table, and Board views.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <div
            className="inline-flex rounded-xl border border-border bg-card p-1 shadow-sm"
            aria-label="Select task view"
          >
            {viewModes.map((mode) => (
              <Button
                key={mode}
                type="button"
                size="sm"
                variant={viewMode === mode ? "primary" : "ghost"}
                aria-pressed={viewMode === mode}
                className="capitalize"
                onClick={() => changeViewMode(mode)}
              >
                {mode}
              </Button>
            ))}
          </div>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={setDefaultView.isPending}
            title="Open this List/Table/Board view by default when anyone opens this list"
            onClick={() => setDefaultView.mutate(viewMode)}
          >
            {listQuery.data.defaultViewId ? "Update default view" : "Set as default view"}
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            aria-pressed={isCurrentViewFavorited}
            disabled={toggleViewFavorite.isPending}
            title={isCurrentViewFavorited ? "Remove this view from favourites" : "Favourite this view"}
            onClick={() => toggleViewFavorite.mutate(undefined)}
          >
            <span aria-hidden="true">{isCurrentViewFavorited ? "★" : "☆"}</span>
          </Button>
          <Button
            type="button"
            variant={advancedOpen ? "primary" : "ghost"}
            size="sm"
            aria-pressed={advancedOpen}
            onClick={() => setAdvancedOpen((current) => !current)}
          >
            Advanced
          </Button>
          <Button type="button" onClick={() => setQuickAddOpen(true)}>
            <span aria-hidden="true">+</span> New task
          </Button>
        </div>
      </div>

      {advancedOpen ? (
        <div className="space-y-4 rounded-[var(--radius)] border border-border bg-card p-4" aria-label="Advanced view settings">
          <div>
            <h2 className="text-sm font-semibold">Filter groups</h2>
            <p className="mt-1 text-xs text-muted-foreground">
              Nested AND/OR conditions, evaluated on the server (in addition to the quick filters below).
            </p>
            <div className="mt-2">
              <FilterBuilder
                group={filterGroup}
                statuses={statuses}
                members={membersQuery.data ?? []}
                tags={tagsQuery.data ?? []}
                onChange={setFilterGroup}
              />
            </div>
          </div>
          <div>
            <h2 className="text-sm font-semibold">Conditional formatting</h2>
            <p className="mt-1 text-xs text-muted-foreground">Applies to the Table view.</p>
            <div className="mt-2">
              <ConditionalFormattingEditor rules={formattingRules} onChange={setFormattingRules} />
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2 border-t border-border pt-3">
            <Button type="button" size="sm" disabled={saveViewConfig.isPending} onClick={() => saveViewConfig.mutate(undefined)}>
              Save to this view
            </Button>
            <Button type="button" variant="outline" size="sm" disabled={saveAsTemplate.isPending} onClick={() => saveAsTemplate.mutate(undefined)}>
              Save as template
            </Button>
          </div>
        </div>
      ) : null}

      <div
        className="flex flex-wrap items-end gap-3 rounded-[var(--radius)] border border-border bg-card p-3"
        aria-label="Filter tasks"
      >
        <label className="grid gap-1 text-xs font-medium text-muted-foreground">
          Search
          <input
            type="search"
            value={search}
            placeholder="Task title"
            className={fieldClassName}
            onChange={(event) => setSearch(event.currentTarget.value)}
          />
        </label>
        <label className="grid gap-1 text-xs font-medium text-muted-foreground">
          Status
          <select
            value={filters.status ?? ""}
            className={fieldClassName}
            onChange={(event) =>
              setFilters((current) => ({ ...current, status: event.target.value || undefined }))
            }
          >
            <option value="">All statuses</option>
            {statuses.map((status) => (
              <option key={status.id} value={status.id}>
                {status.name}
              </option>
            ))}
          </select>
        </label>
        <label className="grid gap-1 text-xs font-medium text-muted-foreground">
          Assignee
          <select
            value={filters.assignee ?? ""}
            className={fieldClassName}
            onChange={(event) =>
              setFilters((current) => ({ ...current, assignee: event.target.value || undefined }))
            }
          >
            <option value="">Anyone</option>
            {(membersQuery.data ?? []).map((member) => (
              <option key={member.userId} value={member.userId}>
                {member.displayName || member.email || member.userId}
              </option>
            ))}
          </select>
        </label>
        <label className="grid gap-1 text-xs font-medium text-muted-foreground">
          Tag
          <select
            value={filters.tag ?? ""}
            className={fieldClassName}
            onChange={(event) =>
              setFilters((current) => ({ ...current, tag: event.target.value || undefined }))
            }
          >
            <option value="">Any tag</option>
            {(tagsQuery.data ?? []).map((tag) => (
              <option key={tag.id} value={tag.id}>
                {tag.name}
              </option>
            ))}
          </select>
        </label>
        <label className="grid gap-1 text-xs font-medium text-muted-foreground">
          Sort
          <select
            value={filters.sort ?? "position"}
            className={fieldClassName}
            onChange={(event) =>
              setFilters((current) => ({
                ...current,
                sort: event.target.value as ListTasksFilters["sort"],
              }))
            }
          >
            {sortOptions.map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
      </div>

      {listIsEmpty ? (
        <EmptyState
          title="This list is empty — add your first task"
          description="Tasks are the unit of work here. Add one below in the status you want it to start in, or use New task for the full form."
        >
          <Button type="button" onClick={() => setQuickAddOpen(true)}>
            <span aria-hidden="true">+</span> New task
          </Button>
        </EmptyState>
      ) : null}

      {viewMode === "list" ? (
        <ListView
          tasks={tasks}
          statuses={statuses}
          listId={listId}
          listIsEmpty={listIsEmpty}
          selection={selection}
          onOpenTask={openTask}
        />
      ) : null}
      {viewMode === "table" ? (
        <TableView
          tasks={tasks}
          statuses={statuses}
          listIsEmpty={listIsEmpty}
          selection={selection}
          formattingRules={formattingRules}
          onOpenTask={openTask}
        />
      ) : null}
      {viewMode === "board" ? (
        <BoardView
          tasks={tasks}
          statuses={statuses}
          listId={listId}
          listIsEmpty={listIsEmpty}
          onOpenTask={openTask}
        />
      ) : null}

      {viewMode === "board" ? null : (
        <BulkActionBar
          selectedIds={selection.selectedIds}
          statuses={statuses}
          onClear={selection.clear}
        />
      )}

      {quickAddOpen ? (
        <QuickAddTask
          listId={listId}
          statuses={statuses}
          onClose={() => setQuickAddOpen(false)}
          onCreated={openTask}
        />
      ) : null}

      <TaskDetailPanel
        taskId={selectedTaskId}
        open={Boolean(selectedTaskId)}
        statuses={statuses}
        onOpenTask={openTask}
        onClose={() => openTask(null)}
      />
    </section>
  );
}
