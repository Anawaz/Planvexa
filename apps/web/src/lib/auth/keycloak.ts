export const keycloakConfig = {
  url: process.env.KEYCLOAK_URL ?? process.env.NEXT_PUBLIC_KEYCLOAK_URL ?? "http://localhost:8081",
  realm: process.env.KEYCLOAK_REALM ?? process.env.NEXT_PUBLIC_KEYCLOAK_REALM ?? "planvexa",
  clientId: process.env.KEYCLOAK_WEB_CLIENT_ID ?? process.env.NEXT_PUBLIC_KEYCLOAK_CLIENT_ID ?? "planvexa-web",
  appUrl: process.env.PLANVEXA_WEB_URL ?? "http://localhost:3000",
};

export function issuerUrl() {
  return `${keycloakConfig.url.replace(/\/$/, "")}/realms/${keycloakConfig.realm}`;
}

/**
 * Absolute app URL for a path. Never derive these from `request.url`: behind a reverse proxy
 * (the Aspire dev proxy, any ingress) that is the internal origin, and Keycloak then rejects the
 * redirect as an invalid URI.
 */
export function appUrl(path: string) {
  return new URL(path, `${keycloakConfig.appUrl.replace(/\/$/, "")}/`).toString();
}

export function redirectUri() {
  return appUrl("/auth/callback");
}
