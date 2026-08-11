"use client";

import { useEffect } from "react";
import { recordRecentItem } from "@/lib/work/client";

/**
 * Fire-and-forget: records/bumps a "recently viewed" entry once a detail view actually has a resource to
 * show. Failures are swallowed — this is a nice-to-have, not something that should surface an error to
 * the user opening a task/document/dashboard/list.
 */
export function useRecordRecentView(resourceType: string, resourceId: string | null | undefined, enabled = true) {
  useEffect(() => {
    if (!enabled || !resourceId) {
      return;
    }

    recordRecentItem(resourceType, resourceId).catch(() => {
      // best-effort
    });
  }, [resourceType, resourceId, enabled]);
}
