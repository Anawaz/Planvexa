"use client";

import { Button, buttonStyles } from "@/components/ui/Button";
import { keycloakConfig } from "@/lib/auth/keycloak";

/**
 * Rendered by AuthenticatedAppLayout instead of the normal shell when AppContext's `mfaRequired` is
 * true (WorkspaceResolutionMiddleware blocked a workspace-scoped request with the mfa-required
 * problem type). Explains what happened and the one thing the User can do next — set up a second
 * factor in the identity provider account console, then come back and sign in again.
 */
export function MfaRequiredScreen() {
  const accountConsoleUrl = `${keycloakConfig.url.replace(/\/$/, "")}/realms/${keycloakConfig.realm}/account`;

  return (
    <div className="grid min-h-screen place-items-center bg-background px-4">
      <div className="max-w-md rounded-[var(--radius)] border border-border bg-card p-6 text-center shadow-sm">
        <h1 className="text-lg font-semibold">Multi-factor authentication required</h1>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">
          This workspace requires every member to sign in with a second factor. Set up an authenticator
          in your account, then sign in again to continue.
        </p>
        <div className="mt-5 flex flex-col items-center gap-2 sm:flex-row sm:justify-center">
          <a href={accountConsoleUrl} target="_blank" rel="noreferrer" className={buttonStyles()}>
            Set up multi-factor authentication
          </a>
          <Button variant="outline" onClick={() => window.location.assign("/login")}>
            Sign in again
          </Button>
        </div>
      </div>
    </div>
  );
}
