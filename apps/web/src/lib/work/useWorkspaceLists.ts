import { useQueries, useQuery } from "@tanstack/react-query";
import { listLists, listSpaces } from "./client";
import { workKeys } from "./queries";

/** Every list in the workspace, flattened, so a picker can target one regardless of its space. */
export function useWorkspaceLists() {
  const spacesQuery = useQuery({ queryKey: workKeys.spaces(), queryFn: listSpaces });
  const spaces = spacesQuery.data ?? [];
  const listQueries = useQueries({
    queries: spaces.map((space) => ({
      queryKey: workKeys.lists(space.id),
      queryFn: () => listLists(space.id),
    })),
  });

  return listQueries.flatMap((query, index) =>
    (query.data ?? []).map((list) => ({
      id: list.id,
      label: spaces[index].name + " / " + list.name,
    })),
  );
}
