/**
 * Instance branding, set by a host administrator under /host/settings and served anonymously by
 * `GET /api/v1/public/registration-policy` — anonymously because the sign-in page has to brand itself
 * before anyone has a session.
 */
export type InstanceBranding = {
  /** Never blank: falls back to {@link DEFAULT_INSTANCE_NAME} when the operator has not set one. */
  instanceName: string;
  logoUrl: string | null;
  supportEmail: string | null;
  /** Whether to offer self-service signup at all (Registration gate). */
  allowSelfRegistration: boolean;
};

export const DEFAULT_INSTANCE_NAME = "Planvexa";

/**
 * Shared fallback. Self-registration defaults to TRUE here on purpose: this value is used when the API
 * could not be reached, and hiding a real signup path on a transient outage is worse than briefly
 * showing one that then rejects you.
 */
export const FALLBACK_BRANDING: InstanceBranding = {
  instanceName: DEFAULT_INSTANCE_NAME,
  logoUrl: null,
  supportEmail: null,
  allowSelfRegistration: true,
};

/** Normalises the API payload, collapsing null/blank to the product default. */
export function toBranding(payload: {
  instanceName?: string | null;
  logoUrl?: string | null;
  supportEmail?: string | null;
  allowSelfRegistration?: boolean;
}): InstanceBranding {
  return {
    instanceName: payload.instanceName?.trim() || DEFAULT_INSTANCE_NAME,
    logoUrl: payload.logoUrl?.trim() || null,
    supportEmail: payload.supportEmail?.trim() || null,
    allowSelfRegistration: payload.allowSelfRegistration ?? true,
  };
}
