"use client";

import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import { QueryState } from "@/components/ui/QueryState";
import { getInstanceSettings, updateInstanceSettings } from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";
import type { UpdateInstanceSettingsInput, WorkspaceCreationPolicy } from "@/lib/host/types";
import { IsoDateTime, MutationError, PageHeader, panelClassName, selectClassName, textInputClassName } from "./host-ui";

export function SettingsPageClient() {
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState("");
  // Two independent drafts because the two forms submit independently — the API treats an omitted
  // field as "leave it alone", so neither form can clobber the other's values.
  const [accessDraft, setAccessDraft] = useState<UpdateInstanceSettingsInput | null>(null);
  const [brandingDraft, setBrandingDraft] = useState<UpdateInstanceSettingsInput | null>(null);

  const settingsQuery = useQuery({ queryKey: hostKeys.settings(), queryFn: getInstanceSettings });

  const saveMutation = useMutation({
    mutationFn: updateInstanceSettings,
    onSuccess: (settings) => {
      setSaved("Saved. This applies to the whole installation immediately.");
      setAccessDraft(null);
      setBrandingDraft(null);
      queryClient.setQueryData(hostKeys.settings(), settings);
      void queryClient.invalidateQueries({ queryKey: hostKeys.all });
    },
  });

  const settings = settingsQuery.data;

  const access: UpdateInstanceSettingsInput = accessDraft ?? {
    allowSelfRegistration: settings?.allowSelfRegistration ?? false,
    workspaceCreationPolicy: settings?.workspaceCreationPolicy ?? "Anyone",
  };

  const branding: UpdateInstanceSettingsInput = brandingDraft ?? {
    instanceName: settings?.instanceName ?? "",
    logoUrl: settings?.logoUrl ?? "",
    supportEmail: settings?.supportEmail ?? "",
  };

  function submit(event: FormEvent<HTMLFormElement>, input: UpdateInstanceSettingsInput) {
    event.preventDefault();
    setSaved("");
    saveMutation.mutate(input);
  }

  return (
    <section aria-labelledby="host-settings-title" className="space-y-6">
      <PageHeader
        id="host-settings-title"
        eyebrow="Host administration"
        title="Instance settings"
        description="Settings for the whole installation. Workspace-level settings stay in each workspace's own settings area."
      />

      {saved ? (
        <p role="status" className="rounded-lg bg-primary/10 px-4 py-3 text-sm font-medium text-primary">
          {saved}
        </p>
      ) : null}

      <MutationError error={saveMutation.error} />

      <QueryState query={settingsQuery} loadingLabel="Loading instance settings…">
        {settings ? (
          <div className="space-y-6">
            <form onSubmit={(event) => submit(event, access)} className={`${panelClassName} space-y-4 p-4`}>
              <div>
                <h2 className="text-sm font-semibold">Access</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  Who can get onto this server, and who can start a new workspace on it.
                </p>
              </div>

              <label className="flex items-start gap-3 rounded-xl border border-border bg-background p-4">
                <input
                  type="checkbox"
                  className="mt-1"
                  checked={access.allowSelfRegistration ?? false}
                  onChange={(event) =>
                    setAccessDraft({ ...access, allowSelfRegistration: event.target.checked })
                  }
                />
                <span>
                  <span className="text-sm font-medium">Allow self-registration</span>
                  <span className="mt-1 block text-sm text-muted-foreground">
                    When off, only people with a pending workspace invitation can create an account.
                    Anyone who already has one keeps their access.
                  </span>
                </span>
              </label>

              <div className="grid gap-2">
                <label htmlFor="workspace-creation-policy" className="text-sm font-medium">
                  Who may create workspaces
                </label>
                <select
                  id="workspace-creation-policy"
                  value={access.workspaceCreationPolicy ?? "Anyone"}
                  onChange={(event) =>
                    setAccessDraft({
                      ...access,
                      workspaceCreationPolicy: event.target.value as WorkspaceCreationPolicy,
                    })
                  }
                  className={selectClassName}
                >
                  <option value="Anyone">Anyone with an account</option>
                  <option value="HostAdminsOnly">Host administrators only</option>
                </select>
                <p className="text-sm text-muted-foreground">
                  Existing workspaces are unaffected either way.
                </p>
              </div>

              <Button type="submit" size="sm" disabled={saveMutation.isPending}>
                {saveMutation.isPending ? "Saving…" : "Save access settings"}
              </Button>
            </form>

            <form onSubmit={(event) => submit(event, branding)} className={`${panelClassName} space-y-4 p-4`}>
              <div>
                <h2 className="text-sm font-semibold">Branding and support</h2>
                <p className="mt-1 text-sm text-muted-foreground">
                  Shown on the sign-in page before anyone has a session. Clear a field to fall back to
                  the Planvexa default.
                </p>
              </div>

              <div className="grid gap-2">
                <label htmlFor="instance-name" className="text-sm font-medium">Instance name</label>
                <input
                  id="instance-name"
                  value={branding.instanceName ?? ""}
                  maxLength={200}
                  onChange={(event) => setBrandingDraft({ ...branding, instanceName: event.target.value })}
                  placeholder="Planvexa"
                  className={textInputClassName}
                />
              </div>

              <div className="grid gap-2">
                <label htmlFor="logo-url" className="text-sm font-medium">Logo URL</label>
                <input
                  id="logo-url"
                  type="url"
                  value={branding.logoUrl ?? ""}
                  maxLength={500}
                  onChange={(event) => setBrandingDraft({ ...branding, logoUrl: event.target.value })}
                  placeholder="https://example.com/logo.png"
                  className={textInputClassName}
                />
                <p className="text-sm text-muted-foreground">Must be an absolute http(s) URL.</p>
              </div>

              <div className="grid gap-2">
                <label htmlFor="support-email" className="text-sm font-medium">Support email</label>
                <input
                  id="support-email"
                  type="email"
                  value={branding.supportEmail ?? ""}
                  maxLength={320}
                  onChange={(event) => setBrandingDraft({ ...branding, supportEmail: event.target.value })}
                  placeholder="support@example.com"
                  className={textInputClassName}
                />
              </div>

              <Button type="submit" size="sm" disabled={saveMutation.isPending}>
                {saveMutation.isPending ? "Saving…" : "Save branding"}
              </Button>
            </form>

            {settings.updatedAtUtc ? (
              <p className="text-sm text-muted-foreground">
                Last changed <IsoDateTime value={settings.updatedAtUtc} />.
              </p>
            ) : null}
          </div>
        ) : null}
      </QueryState>
    </section>
  );
}
