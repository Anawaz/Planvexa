export const goalKeys = {
  all: ["goals"] as const,
  list: (folderId?: string) => [...goalKeys.all, "list", folderId ?? "all"] as const,
  detail: (id: string) => [...goalKeys.all, "detail", id] as const,
  comments: (id: string) => [...goalKeys.all, "comments", id] as const,
  folders: () => [...goalKeys.all, "folders"] as const,
};
