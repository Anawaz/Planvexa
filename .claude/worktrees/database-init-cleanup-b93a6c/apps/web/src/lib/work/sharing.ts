import { apiClient } from "../api-client";

// Mirrors SharedContracts.Workspaces.PermissionLevel / ResourcePrincipalType and the
// ResourceSharingEndpoints routes (ADR-0003).
export type ResourceType = "space" | "folder" | "list" | "task";
export type PrincipalType = "user" | "team" | "role";
export type PermissionLevel = "view" | "comment" | "edit" | "fullEdit" | "share" | "manage";

export const permissionLevels: PermissionLevel[] = ["view", "comment", "edit", "fullEdit", "share", "manage"];

export type ResourcePermissionGrant = {
  id: string;
  resourceType: string;
  resourceId: string;
  principalType: string;
  principalId: string;
  level: PermissionLevel;
  grantedByUserId: string;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
};

export function listResourcePermissions(resourceType: ResourceType, resourceId: string) {
  return apiClient.get<ResourcePermissionGrant[]>(`/api/v1/resources/${resourceType}/${resourceId}/permissions`);
}

export function grantResourcePermission(
  resourceType: ResourceType,
  resourceId: string,
  principalType: PrincipalType,
  principalId: string,
  level: PermissionLevel,
) {
  return apiClient.post<ResourcePermissionGrant>(`/api/v1/resources/${resourceType}/${resourceId}/permissions`, {
    principalType,
    principalId,
    level,
  });
}

export function revokeResourcePermission(
  resourceType: ResourceType,
  resourceId: string,
  principalType: PrincipalType,
  principalId: string,
) {
  return apiClient.delete<void>(`/api/v1/resources/${resourceType}/${resourceId}/permissions/${principalType}/${principalId}`);
}

export function setResourcePrivate(resourceType: ResourceType, resourceId: string, isPrivate: boolean) {
  return apiClient.patch<{ isPrivate: boolean }>(`/api/v1/resources/${resourceType}/${resourceId}/private`, {
    isPrivate,
  });
}
