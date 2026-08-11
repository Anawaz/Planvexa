const typeLabels: Record<string, string> = {
  space: "Space",
  folder: "Folder",
  list: "List",
  task: "Task",
  document: "Document",
  dashboard: "Dashboard",
  chatchannel: "Chat channel",
  form: "Form",
  savedview: "View",
};

/** Shared by the Favourites and Recent nav sections — both store the same free-form resource_type. */
export function recentItemHref(resourceType: string, resourceId: string) {
  switch (resourceType.toLowerCase()) {
    case "space":
    case "folder":
      return "/app/spaces";
    case "list":
      return `/app/lists/${resourceId}`;
    case "task":
      // No standalone task URL — the same detail drawer opens from My Work.
      return `/app/my-work?task=${resourceId}`;
    case "document":
      return `/app/documents/${resourceId}`;
    case "dashboard":
      return `/app/dashboards/${resourceId}`;
    case "chatchannel":
      return `/app/chat?channel=${resourceId}`;
    case "form":
      return "/app/forms";
    default:
      return "/app/spaces";
  }
}

export function recentItemLabel(resourceType: string) {
  return typeLabels[resourceType.toLowerCase()] ?? resourceType;
}
