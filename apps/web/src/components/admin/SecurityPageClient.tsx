"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { FormEvent } from "react";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { getSecuritySettings, updateSecuritySettings } from "@/lib/admin/client";
import { adminKeys } from "@/lib/admin/queries";
import type { UpdateSecuritySettingsInput } from "@/lib/admin/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";
import { PageHeader, panelClassName, textInputClassName } from "./admin-ui";

const emptyDraft: UpdateSecuritySettingsInput = {
  ssoEnabled: false,
  samlEntityId: "",
  samlMetadataUrl: "",
  scimEnabled: false,
  scimTokenSet: false,
  mfaRequired: false,
  scimToken: "",
};

function toggleClassName(enabled: boolean) {
  return cn(
    "flex items-start gap-3 rounded-xl border p-4 focus-within:outline focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring",
    enabled ? "border-primary bg-primary/10" : "border-border bg-background",
  );
}

export function SecurityPageClient() {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [localDraft, setLocalDraft] = useState<UpdateSecuritySettingsInput | null>(null);
  const [statusMessage, setStatusMessage] = useState("");
  const settingsQuery = useQuery({
    queryKey: adminKeys.security(workspaceId),
    queryFn: getSecuritySettings,
  });
  const updateMutation = useMutation({
    mutationFn: updateSecuritySettings,
    onSuccess: (settings) => {
      setLocalDraft({ ...settings, scimToken: "" });
      setStatusMessage("Security settings saved. Enforcement remains with your IdP.");
      void queryClient.invalidateQueries({ queryKey: adminKeys.security(workspaceId) });
      void queryClient.invalidateQueries({ queryKey: adminKeys.auditRoot(workspaceId) });
    },
  });
  const draft: UpdateSecuritySettingsInput =
    localDraft ??
    (settingsQuery.data ? { ...settingsQuery.data, scimToken: "" } : emptyDraft);

  function setDraft(update: (current: UpdateSecuritySettingsInput) => UpdateSecuritySettingsInput) {
    setLocalDraft(update(draft));
  }

  function submitSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    updateMutation.mutate(draft);
  }

  return (
    <section aria-labelledby="security-title" className="space-y-6">
      <PageHeader
        id="security-title"
        eyebrow="Enterprise security"
        title="Security"
        description="Configure SSO, SCIM, and workspace MFA requirements."
      />

      <div className="rounded-[var(--radius)] border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800 dark:border-blue-900 dark:bg-blue-950 dark:text-blue-200">
        These controls are config only; enforcement handled by your IdP and backend policies.
      </div>

      {statusMessage ? (
        <p role="status" className="rounded-lg bg-primary/10 px-4 py-3 text-sm font-medium text-primary">
          {statusMessage}
        </p>
      ) : null}

      {settingsQuery.isLoading ? (
        <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>
          Loading security settings…
        </section>
      ) : (
        <form onSubmit={submitSettings} className={cn(panelClassName, "space-y-6 p-5")}>
          <fieldset className="space-y-4">
            <legend className="text-lg font-semibold">Single sign-on</legend>
            <label className={toggleClassName(draft.ssoEnabled)}>
              <input
                type="checkbox"
                checked={draft.ssoEnabled}
                onChange={(event) => setDraft((current) => ({ ...current, ssoEnabled: event.target.checked }))}
                className="mt-1 size-4 rounded border-border accent-[var(--primary)]"
              />
              <span>
                <span className="block text-sm font-semibold">Enable SAML SSO</span>
                <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                  Route authentication through the workspace identity provider once backend enforcement is wired.
                </span>
              </span>
            </label>
            <div className="grid gap-4 lg:grid-cols-2">
              <label htmlFor="saml-entity-id" className="grid gap-2 text-sm font-medium">
                SAML entity ID
                <input
                  id="saml-entity-id"
                  value={draft.samlEntityId ?? ""}
                  onChange={(event) => setDraft((current) => ({ ...current, samlEntityId: event.target.value }))}
                  className={textInputClassName}
                  placeholder="https://idp.example.com/metadata/planvexa"
                />
              </label>
              <label htmlFor="saml-metadata-url" className="grid gap-2 text-sm font-medium">
                Metadata URL
                <input
                  id="saml-metadata-url"
                  type="url"
                  value={draft.samlMetadataUrl ?? ""}
                  onChange={(event) => setDraft((current) => ({ ...current, samlMetadataUrl: event.target.value }))}
                  className={textInputClassName}
                  placeholder="https://idp.example.com/saml/metadata.xml"
                />
              </label>
            </div>
          </fieldset>

          <fieldset className="space-y-4">
            <legend className="text-lg font-semibold">Directory sync</legend>
            <label className={toggleClassName(draft.scimEnabled)}>
              <input
                type="checkbox"
                checked={draft.scimEnabled}
                onChange={(event) => setDraft((current) => ({ ...current, scimEnabled: event.target.checked }))}
                className="mt-1 size-4 rounded border-border accent-[var(--primary)]"
              />
              <span>
                <span className="block text-sm font-semibold">Enable SCIM provisioning</span>
                <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                  Provision and deprovision workspace members from the identity provider.
                </span>
              </span>
            </label>
            <div className="grid gap-2">
              <label htmlFor="scim-token" className="text-sm font-medium">
                Set SCIM token
              </label>
              <input
                id="scim-token"
                type="password"
                autoComplete="new-password"
                value={draft.scimToken ?? ""}
                onChange={(event) => setDraft((current) => ({ ...current, scimToken: event.target.value }))}
                className={textInputClassName}
                aria-describedby="scim-token-help scim-token-state"
                placeholder="Paste a new token to rotate it"
              />
              <p id="scim-token-state" className="text-sm font-medium">
                Token state: {draft.scimTokenSet ? "Set" : "Not set"}
              </p>
              <p id="scim-token-help" className="text-xs leading-5 text-muted-foreground">
                The API only records whether a token exists and never returns or echoes token secrets.
              </p>
            </div>
          </fieldset>

          <fieldset className="space-y-4">
            <legend className="text-lg font-semibold">Workspace access policy</legend>
            <label className={toggleClassName(draft.mfaRequired)}>
              <input
                type="checkbox"
                checked={draft.mfaRequired}
                onChange={(event) => setDraft((current) => ({ ...current, mfaRequired: event.target.checked }))}
                className="mt-1 size-4 rounded border-border accent-[var(--primary)]"
              />
              <span>
                <span className="block text-sm font-semibold">Require MFA for members</span>
                <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                  Blocks every member from this workspace until their session has completed a second
                  factor — enforced on the backend for every request, not only shown here. A member
                  completes MFA by setting up an authenticator in their identity provider account and
                  signing in again.
                </span>
              </span>
            </label>
          </fieldset>

          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-5">
            <p className="text-sm text-muted-foreground">
              SSO and SCIM are stored per workspace; enforcement stays with your identity provider.
            </p>
            <Button type="submit" disabled={updateMutation.isPending}>
              Save security settings
            </Button>
          </div>
        </form>
      )}
    </section>
  );
}
