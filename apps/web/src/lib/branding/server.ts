import { FALLBACK_BRANDING, toBranding, type InstanceBranding } from "./types";

const API_BASE_URL = (process.env.API_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080").replace(/\/$/, "");

/**
 * Server-side branding lookup for the pre-auth surfaces (landing page, sign-in page, document title).
 * Talks to the API directly rather than through the BFF proxy: these render before any session exists,
 * and the proxy's job is attaching a session token.
 *
 * `no-store`, not a revalidate window: a host administrator who renames the instance and reloads must
 * see the new name, and "did my change save?" is exactly the confusion a cache would cause here.
 * ponytail: one extra API call per pre-auth render — swap in a short revalidate if these pages ever
 *  become hot enough for it to show up in latency.
 *
 * Never throws. An unreachable API falls back to the product defaults, because a branding lookup must
 * not be able to take down the sign-in page.
 */
export async function getInstanceBranding(): Promise<InstanceBranding> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/v1/public/registration-policy`, { cache: "no-store" });
    if (!response.ok) return FALLBACK_BRANDING;
    return toBranding(await response.json());
  } catch {
    return FALLBACK_BRANDING;
  }
}
