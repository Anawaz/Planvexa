"use client";

import { useQuery } from "@tanstack/react-query";
import { apiClient } from "@/lib/api-client";
import { FALLBACK_BRANDING, toBranding, type InstanceBranding } from "./types";

export const brandingKeys = {
  all: ["branding"] as const,
  instance: () => [...brandingKeys.all, "instance"] as const,
};

/**
 * Instance branding for the authenticated shells (workspace sidebar/topbar, host console).
 *
 * `noWorkspace` because this endpoint is anonymous and instance-wide — sending `X-Workspace` would
 * make the API resolve a workspace it does not need, and the host console has none to send anyway.
 *
 * Returns the fallback while loading rather than undefined, so every call site can render the wordmark
 * immediately instead of flashing a gap: the name is one short string, and "Planvexa" for a moment
 * beats an empty header on every navigation.
 */
export function useInstanceBranding(): InstanceBranding {
  const query = useQuery({
    queryKey: brandingKeys.instance(),
    queryFn: async () =>
      toBranding(
        await apiClient.get<{
          instanceName?: string | null;
          logoUrl?: string | null;
          supportEmail?: string | null;
          allowSelfRegistration?: boolean;
        }>("/public/registration-policy", { noWorkspace: true }),
      ),
    // One cache entry shared by every wordmark on the page. Five minutes: long enough that it is not a
    // per-navigation request, short enough that a rename shows up without a hard reload.
    staleTime: 5 * 60_000,
  });

  return query.data ?? FALLBACK_BRANDING;
}
