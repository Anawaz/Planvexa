import { apiClient } from "../api-client";

// Search fans out across every module (WorkManagement, Documents, Collaboration, Chat, Tenancy,
// Reporting, Forms) — see apps/api's SearchAggregator and each module's ISearchProvider implementation.
export type SearchResultType =
  | "Task"
  | "List"
  | "Folder"
  | "Space"
  | "Document"
  | "Comment"
  | "ChatChannel"
  | "ChatMessage"
  | "Member"
  | "Team"
  | "Dashboard"
  | "Form";

/** One flat hit from `GET /api/v1/search` — workspace scoped by the ambient headers. */
export type SearchResult = {
  type: SearchResultType;
  id: string;
  title: string;
  subtitle?: string | null;
  listId?: string | null;
};

export async function search(term: string, limit?: number) {
  const query = new URLSearchParams({ q: term });
  if (limit !== undefined) query.append("limit", String(limit));
  return apiClient.get<SearchResult[]>(`/api/v1/search?${query.toString()}`);
}

/**
 * Where a hit navigates. A task (and a Comment, which links to its owning task) opens its list with
 * `?task=` so the detail drawer opens on arrival. A Comment/ChatMessage's own `id` is already the
 * navigable target (owning task/channel id) — see SearchHit's doc comment in SharedContracts.
 */
export function searchResultHref(result: SearchResult) {
  switch (result.type) {
    case "Task":
    case "Comment":
      return result.listId ? `/app/lists/${result.listId}?task=${result.id}` : "/app/spaces";
    case "List":
      return result.listId ? `/app/lists/${result.listId}` : "/app/spaces";
    case "Folder":
    case "Space":
      return "/app/spaces";
    case "Document":
      return `/app/documents/${result.id}`;
    case "Dashboard":
      return `/app/dashboards/${result.id}`;
    case "Form":
      return "/app/forms";
    case "ChatChannel":
    case "ChatMessage":
      return `/app/chat?channel=${result.id}`;
    case "Member":
      return "/app/members";
    case "Team":
      return "/app/members";
    default:
      return "/app/spaces";
  }
}
