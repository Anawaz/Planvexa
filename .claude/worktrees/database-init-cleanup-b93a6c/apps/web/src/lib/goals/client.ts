import { apiClient } from "../api-client";
import type {
  CreateGoalInput,
  Goal,
  GoalComment,
  GoalDetail,
  GoalFolder,
  UpdateGoalInput,
} from "./types";

export async function listGoals(folderId?: string) {
  const suffix = folderId ? `?folderId=${folderId}` : "";
  return apiClient.get<Goal[]>(`/api/v1/goals${suffix}`);
}

export async function getGoal(id: string) {
  return apiClient.get<GoalDetail>(`/api/v1/goals/${id}`);
}

export async function createGoal(input: CreateGoalInput) {
  return apiClient.post<Goal>("/api/v1/goals", input);
}

export async function updateGoal(id: string, input: UpdateGoalInput) {
  return apiClient.put<Goal>(`/api/v1/goals/${id}`, input);
}

export async function deleteGoal(id: string) {
  await apiClient.delete<void>(`/api/v1/goals/${id}`);
}

export async function linkGoalTask(id: string, taskId: string) {
  return apiClient.post<Goal>(`/api/v1/goals/${id}/linked-tasks`, { taskId });
}

export async function unlinkGoalTask(id: string, taskId: string) {
  return apiClient.delete<Goal>(`/api/v1/goals/${id}/linked-tasks/${taskId}`);
}

export async function listGoalFolders() {
  return apiClient.get<GoalFolder[]>("/api/v1/goal-folders");
}

export async function createGoalFolder(name: string) {
  return apiClient.post<GoalFolder>("/api/v1/goal-folders", { name });
}

export async function listGoalComments(goalId: string) {
  return apiClient.get<GoalComment[]>(`/api/v1/goals/${goalId}/comments`);
}

export async function addGoalComment(goalId: string, body: string) {
  return apiClient.post<GoalComment>(`/api/v1/goals/${goalId}/comments`, { body });
}
