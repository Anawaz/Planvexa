export const aiKeys = {
  all: ["ai"] as const,
  assistRoot: () => [...aiKeys.all, "assist"] as const,
  summary: (taskId: string) => [...aiKeys.assistRoot(), "summary", taskId] as const,
  subtasks: (taskId: string) => [...aiKeys.assistRoot(), "subtasks", taskId] as const,
  priority: (taskId: string) => [...aiKeys.assistRoot(), "priority", taskId] as const,
  usage: () => [...aiKeys.all, "usage"] as const,
  devicesRoot: () => [...aiKeys.all, "devices"] as const,
  devices: () => [...aiKeys.devicesRoot(), "list"] as const,
  sync: (sinceUtc?: string) => [...aiKeys.all, "sync", sinceUtc ?? "latest"] as const,
  retention: () => [...aiKeys.all, "retention"] as const,
  settings: () => [...aiKeys.all, "settings"] as const,
  featureStatus: () => [...aiKeys.all, "feature-status"] as const,
};
