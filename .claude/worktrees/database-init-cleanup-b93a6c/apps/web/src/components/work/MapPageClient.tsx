"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { EmptyState } from "@/components/ui/EmptyState";
import { listEffectiveCustomFields, listLists, listLocationValues, listSpaces } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";

/**
 * Map view. Location custom fields store a free-text address string (not lat/lng) for the Location custom
 * field type, so a real interactive map tile widget needs a maps-provider credential decision that
 * isn't this agent's to make -- per the design brief, this is implemented as a sortable/groupable LIST
 * of tasks-with-a-Location-value instead. Real map-tile rendering is a documented gap: swap this for a
 * tile-based map once a provider (Mapbox/Google/MapLibre + API key) is chosen.
 */
export function MapPageClient() {
  const [listId, setListId] = useState("");
  const [definitionId, setDefinitionId] = useState("");
  const [groupByLocation, setGroupByLocation] = useState(true);

  const spacesQuery = useQuery({ queryKey: workKeys.spaces(), queryFn: listSpaces });
  const spaces = spacesQuery.data ?? [];

  const listsQueries = useQuery({
    queryKey: [...workKeys.all, "map", "all-lists", spaces.map((space) => space.id)],
    queryFn: async () => {
      const perSpace = await Promise.all(spaces.map((space) => listLists(space.id)));
      return perSpace.flat();
    },
    enabled: spaces.length > 0,
  });
  const lists = listsQueries.data ?? [];
  const effectiveListId = listId || lists[0]?.id || "";

  const fieldsQuery = useQuery({
    queryKey: workKeys.listCustomFields(effectiveListId),
    queryFn: () => listEffectiveCustomFields(effectiveListId),
    enabled: Boolean(effectiveListId),
  });
  const locationFields = (fieldsQuery.data ?? []).filter((field) => field.type === "Location");
  const effectiveDefinitionId = definitionId || locationFields[0]?.id || "";

  const locationsQuery = useQuery({
    queryKey: workKeys.locations(effectiveListId, effectiveDefinitionId),
    queryFn: () => listLocationValues(effectiveListId, effectiveDefinitionId),
    enabled: Boolean(effectiveListId) && Boolean(effectiveDefinitionId),
  });
  const locations = useMemo(() => locationsQuery.data ?? [], [locationsQuery.data]);

  const grouped = useMemo(() => {
    if (!groupByLocation) {
      return null;
    }
    const map = new Map<string, typeof locations>();
    for (const item of locations) {
      map.set(item.location, [...(map.get(item.location) ?? []), item]);
    }
    return [...map.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [groupByLocation, locations]);

  return (
    <section className="space-y-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-wide text-muted-foreground">Views</p>
        <h1 className="text-3xl font-semibold tracking-tight">Map</h1>
        <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
          Tasks with a Location value, as a sortable/groupable list. Not an interactive map — real
          map-tile rendering needs a maps-provider API key that this environment does not have; the
          Location field itself stores a free-text address, not coordinates.
        </p>
      </div>

      <div className="flex flex-wrap items-end gap-3 rounded-2xl border border-border bg-card p-4">
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-xs font-medium text-muted-foreground">List</span>
          <select
            className="h-9 min-w-[14rem] rounded-lg border border-border bg-background px-2 text-sm"
            value={effectiveListId}
            onChange={(event) => {
              setListId(event.target.value);
              setDefinitionId("");
            }}
          >
            {lists.length === 0 ? <option value="">No lists</option> : null}
            {lists.map((list) => (
              <option key={list.id} value={list.id}>
                {list.name}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1 text-sm">
          <span className="text-xs font-medium text-muted-foreground">Location field</span>
          <select
            className="h-9 min-w-[12rem] rounded-lg border border-border bg-background px-2 text-sm"
            value={effectiveDefinitionId}
            onChange={(event) => setDefinitionId(event.target.value)}
            disabled={locationFields.length === 0}
          >
            {locationFields.length === 0 ? <option value="">No Location field on this list</option> : null}
            {locationFields.map((field) => (
              <option key={field.id} value={field.id}>
                {field.name}
              </option>
            ))}
          </select>
        </label>
        <label className="ml-auto inline-flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="size-4 accent-[var(--primary)]"
            checked={groupByLocation}
            onChange={(event) => setGroupByLocation(event.target.checked)}
          />
          Group by location
        </label>
      </div>

      {!effectiveDefinitionId ? (
        <EmptyState
          title="No Location field yet"
          description="Add a Location-type custom field to a list (Settings → Custom fields) to use this view."
        />
      ) : locationsQuery.isLoading ? (
        <p className="p-4 text-sm text-muted-foreground">Loading locations…</p>
      ) : locations.length === 0 ? (
        <EmptyState
          title="No tasks have a location yet"
          description="Set a value for this Location field on a task and it will show up here."
        />
      ) : grouped ? (
        <div className="space-y-4">
          {grouped.map(([location, items]) => (
            <article key={location} className="rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm">
              <h2 className="text-sm font-semibold">{location}</h2>
              <ul className="mt-2 space-y-1 text-sm text-muted-foreground">
                {items.map((item) => (
                  <li key={item.taskId}>{item.taskTitle}</li>
                ))}
              </ul>
            </article>
          ))}
        </div>
      ) : (
        <ol className="space-y-2">
          {[...locations]
            .sort((a, b) => a.taskTitle.localeCompare(b.taskTitle))
            .map((item) => (
              <li
                key={item.taskId}
                className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-card px-4 py-3 text-sm"
              >
                <span className="font-medium">{item.taskTitle}</span>
                <span className="text-muted-foreground">{item.location}</span>
              </li>
            ))}
        </ol>
      )}
    </section>
  );
}
