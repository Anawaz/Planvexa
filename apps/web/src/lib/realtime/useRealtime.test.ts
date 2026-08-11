import { describe, expect, it } from "vitest";
import { QueryClient } from "@tanstack/react-query";
import { collabKeys } from "@/lib/collab/queries";
import { planningKeys } from "@/lib/planning/queries";
import { timeKeys } from "@/lib/time/queries";
import { invalidateFor } from "./useRealtime";

/**
 * Realtime gap fix: NotificationPublisher now broadcasts a "Notification" RealtimeEvent keyed by
 * recipient (entityId), see NotificationPublisher.cs. This only ever reaches every connection in the
 * workspace group (there is no per-user SignalR group), so invalidateFor must ignore events for other
 * recipients and only invalidate the notification queries for the current user's own event.
 */
describe("invalidateFor Notification handling", () => {
  function baseEvent(overrides: Partial<{ entityId: string }> = {}) {
    return {
      workspaceId: "ws-1",
      entityType: "Notification",
      entityId: "user-me",
      action: "created",
      version: null,
      correlationId: "corr-1",
      ...overrides,
    };
  }

  it("invalidates the notification queries when the event is for the current user", () => {
    const queryClient = new QueryClient();
    const invalidated: unknown[][] = [];
    queryClient.invalidateQueries = (options?: { queryKey?: readonly unknown[] }) => {
      invalidated.push([...(options?.queryKey ?? [])]);
      return Promise.resolve();
    };

    invalidateFor(queryClient, baseEvent({ entityId: "user-me" }), "ws-1", "user-me");

    expect(invalidated).toContainEqual([...collabKeys.unreadCount()]);
    expect(invalidated).toContainEqual([...collabKeys.notificationsRoot()]);
  });

  it("ignores a Notification event addressed to a different recipient", () => {
    const queryClient = new QueryClient();
    const invalidated: unknown[][] = [];
    queryClient.invalidateQueries = (options?: { queryKey?: readonly unknown[] }) => {
      invalidated.push([...(options?.queryKey ?? [])]);
      return Promise.resolve();
    };

    invalidateFor(queryClient, baseEvent({ entityId: "someone-else" }), "ws-1", "user-me");

    expect(invalidated).toHaveLength(0);
  });
});

/**
 * Realtime gap fix: TimeLogged/BillableTotals/EstimateVsActual/CustomFormula dashboard widgets read
 * time-entry data via planningKeys, so a TimeEntry event must invalidate planningKeys.all in addition
 * to timeKeys.all — matching the Task case's pattern — or those widgets go stale until a manual refresh.
 */
describe("invalidateFor TimeEntry handling", () => {
  it("invalidates both time and planning queries", () => {
    const queryClient = new QueryClient();
    const invalidated: unknown[][] = [];
    queryClient.invalidateQueries = (options?: { queryKey?: readonly unknown[] }) => {
      invalidated.push([...(options?.queryKey ?? [])]);
      return Promise.resolve();
    };

    invalidateFor(
      queryClient,
      { workspaceId: "ws-1", entityType: "TimeEntry", entityId: "te-1", action: "created", version: null, correlationId: "corr-1" },
      "ws-1",
      "user-me",
    );

    expect(invalidated).toContainEqual([...timeKeys.all]);
    expect(invalidated).toContainEqual([...planningKeys.all]);
  });
});
