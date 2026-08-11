"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import type { FormEvent } from "react";
import { Button } from "@/components/ui/Button";
import {
  createOAuthApplication,
  createToken,
  createWebhook,
  deleteToken,
  deleteWebhook,
  getWebhookDeliveries,
  listOAuthApplications,
  listProviderSettings,
  listTokens,
  listWebhooks,
  revokeOAuthApplication,
  updateProviderSettings,
} from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import type { CreatedOAuthApplication, CreatedToken, CreatedWebhook, IntegrationProviderSettings } from "@/lib/collab/types";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";
import {
  copyToClipboard,
  formatIsoDate,
  formatIsoDateTime,
  numberFormatter,
  panelClassName,
  textInputClassName,
} from "./collab-ui";

// WorkspaceEvent.Types on the API — the only events the dispatcher publishes today.
const webhookEvents = ["task.created", "task.status_changed", "task.assigned", "task.completed"];
const tokenScopes = ["tasks:read", "tasks:write", "docs:read", "forms:write", "webhooks:read", "reports:read"];

// OAuthScopes on the API (src/Modules/Integrations/.../Domain/OAuthApplication.cs).
const oauthScopeVocabulary = ["tasks:read", "tasks:write", "workspace:read", "docs:read", "webhooks:read"];

function toggleItem(items: string[], value: string) {
  return items.includes(value) ? items.filter((item) => item !== value) : [...items, value];
}

export function IntegrationsPageClient() {
  const queryClient = useQueryClient();
  const { workspaceId = "" } = useAppContext();
  const [selectedWebhookId, setSelectedWebhookId] = useState<string | null>(null);
  const [webhookUrl, setWebhookUrl] = useState("");
  const [selectedEvents, setSelectedEvents] = useState<string[]>(["task.created"]);
  const [tokenName, setTokenName] = useState("");
  const [selectedScopes, setSelectedScopes] = useState<string[]>(["tasks:read"]);
  const [tokenExpiryDate, setTokenExpiryDate] = useState("");
  const [createdToken, setCreatedToken] = useState<CreatedToken | null>(null);
  const [createdWebhook, setCreatedWebhook] = useState<CreatedWebhook | null>(null);
  const [copyStatus, setCopyStatus] = useState("");
  const [appName, setAppName] = useState("");
  const [appRedirectUri, setAppRedirectUri] = useState("");
  const [appScopes, setAppScopes] = useState<string[]>(["tasks:read"]);
  const [createdApp, setCreatedApp] = useState<CreatedOAuthApplication | null>(null);
  const [providerDrafts, setProviderDrafts] = useState<Record<string, { configJson: string; secret: string }>>({});
  const webhooksQuery = useQuery({ queryKey: collabKeys.webhooks(workspaceId), queryFn: listWebhooks });
  const webhooks = webhooksQuery.data ?? [];
  const activeWebhookId = selectedWebhookId ?? webhooks[0]?.id ?? "";
  const deliveriesQuery = useQuery({
    queryKey: collabKeys.webhookDeliveries(workspaceId, activeWebhookId),
    queryFn: () => getWebhookDeliveries(activeWebhookId),
    enabled: Boolean(activeWebhookId),
  });
  const tokensQuery = useQuery({ queryKey: collabKeys.tokens(workspaceId), queryFn: listTokens });
  const createWebhookMutation = useMutation({
    mutationFn: createWebhook,
    onSuccess: (webhook) => {
      setWebhookUrl("");
      setSelectedEvents(["task.created"]);
      setSelectedWebhookId(webhook.id);
      // The signing secret is returned once, on creation.
      setCreatedWebhook(webhook);
      void queryClient.invalidateQueries({ queryKey: collabKeys.webhooksRoot(workspaceId) });
    },
  });
  const deleteWebhookMutation = useMutation({
    mutationFn: deleteWebhook,
    onSuccess: () => {
      setSelectedWebhookId(null);
      void queryClient.invalidateQueries({ queryKey: collabKeys.webhooksRoot(workspaceId) });
    },
  });
  const createTokenMutation = useMutation({
    mutationFn: createToken,
    onSuccess: (token) => {
      setCreatedToken(token);
      setTokenName("");
      setSelectedScopes(["tasks:read"]);
      setTokenExpiryDate("");
      void queryClient.invalidateQueries({ queryKey: collabKeys.tokens(workspaceId) });
    },
  });
  const deleteTokenMutation = useMutation({
    mutationFn: deleteToken,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: collabKeys.tokens(workspaceId) });
    },
  });

  const appsQuery = useQuery({ queryKey: collabKeys.oauthApplications(workspaceId), queryFn: listOAuthApplications });
  const createAppMutation = useMutation({
    mutationFn: createOAuthApplication,
    onSuccess: (app) => {
      setCreatedApp(app);
      setAppName("");
      setAppRedirectUri("");
      setAppScopes(["tasks:read"]);
      void queryClient.invalidateQueries({ queryKey: collabKeys.oauthApplications(workspaceId) });
    },
  });
  const revokeAppMutation = useMutation({
    mutationFn: revokeOAuthApplication,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: collabKeys.oauthApplications(workspaceId) }),
  });

  const providersQuery = useQuery({ queryKey: collabKeys.providerSettings(workspaceId), queryFn: listProviderSettings });
  const updateProviderMutation = useMutation({
    mutationFn: ({ provider, configJson, secret, isEnabled }: { provider: string; configJson: string; secret: string; isEnabled: boolean }) =>
      updateProviderSettings(provider, { configJson, secret: secret.length > 0 ? secret : null, isEnabled }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: collabKeys.providerSettings(workspaceId) }),
  });

  function submitApp(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!appName.trim() || !appRedirectUri.trim() || appScopes.length === 0) {
      return;
    }

    createAppMutation.mutate({ name: appName.trim(), redirectUris: [appRedirectUri.trim()], allowedScopes: appScopes });
  }

  function providerDraft(settings: IntegrationProviderSettings) {
    return providerDrafts[settings.provider] ?? { configJson: settings.configJson, secret: "" };
  }

  function setProviderDraft(provider: string, patch: Partial<{ configJson: string; secret: string }>) {
    setProviderDrafts((current) => ({
      ...current,
      [provider]: { ...(current[provider] ?? { configJson: "{}", secret: "" }), ...patch },
    }));
  }

  function submitWebhook(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!webhookUrl.trim() || selectedEvents.length === 0) {
      return;
    }

    createWebhookMutation.mutate({ url: webhookUrl.trim(), eventTypes: selectedEvents });
  }

  function submitToken(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!tokenName.trim() || selectedScopes.length === 0) {
      return;
    }

    createTokenMutation.mutate({
      name: tokenName.trim(),
      scopes: selectedScopes,
      expiresAtUtc: tokenExpiryDate ? new Date(`${tokenExpiryDate}T23:59:59.000Z`).toISOString() : null,
    });
  }

  function copyValue(value: string, message: string) {
    void copyToClipboard(value).then(() => setCopyStatus(message));
  }

  const activeWebhook = webhooks.find((webhook) => webhook.id === activeWebhookId);
  const deliveries = deliveriesQuery.data ?? [];
  const tokens = tokensQuery.data ?? [];
  const apps = appsQuery.data ?? [];
  const providers = providersQuery.data ?? [];

  return (
    <section aria-labelledby="integrations-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Integrations</p>
        <h1 id="integrations-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Integrations
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Manage webhook subscriptions and scoped personal access tokens for this workspace.
        </p>
      </div>

      {copyStatus ? (
        <p role="status" className="rounded-lg bg-primary/10 px-4 py-3 text-sm font-medium text-primary">
          {copyStatus}
        </p>
      ) : null}

      <section className={cn(panelClassName, "p-4")} aria-labelledby="webhooks-title">
        <div className="flex flex-col gap-2 border-b border-border pb-4">
          <h2 id="webhooks-title" className="text-lg font-semibold">
            Webhooks
          </h2>
          <p className="text-sm text-muted-foreground">
            Outbound events are signed with a per-webhook secret and retried by the API.
          </p>
        </div>

        {createdWebhook ? (
          <div role="alert" className="mt-4 rounded-xl border border-primary bg-primary/10 p-4 text-sm">
            <h3 className="font-semibold text-primary">
              Copy this signing secret now — it will only be shown once.
            </h3>
            <code className="mt-2 block break-all rounded-lg bg-background px-3 py-2 text-foreground">
              {createdWebhook.secret}
            </code>
            <div className="mt-3 flex gap-2">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => copyValue(createdWebhook.secret, "Signing secret copied.")}
              >
                Copy secret
              </Button>
              <Button type="button" variant="ghost" size="sm" onClick={() => setCreatedWebhook(null)}>
                Dismiss
              </Button>
            </div>
          </div>
        ) : null}

        <form onSubmit={submitWebhook} className="mt-4 grid gap-4 lg:grid-cols-[1fr_auto]">
          <label className="grid gap-1 text-xs font-medium">
            Endpoint URL
            <input
              type="url"
              value={webhookUrl}
              onChange={(event) => setWebhookUrl(event.target.value)}
              className={textInputClassName}
              placeholder="https://example.com/planvexa/webhook"
            />
          </label>
          <Button type="submit" size="sm" className="self-end" disabled={createWebhookMutation.isPending}>
            Create webhook
          </Button>
          <fieldset className="lg:col-span-2">
            <legend className="mb-2 text-xs font-medium">Event types</legend>
            <div className="flex flex-wrap gap-2">
              {webhookEvents.map((eventType) => (
                <label key={eventType} className="flex items-center gap-2 rounded-full border border-border bg-background px-3 py-1.5 text-sm">
                  <input
                    type="checkbox"
                    checked={selectedEvents.includes(eventType)}
                    onChange={() => setSelectedEvents((current) => toggleItem(current, eventType))}
                    className="size-4 rounded border-border accent-[var(--primary)]"
                  />
                  {eventType}
                </label>
              ))}
            </div>
          </fieldset>
        </form>

        <div className="mt-6 grid gap-6 xl:grid-cols-[22rem_1fr]">
          <div className="space-y-2" aria-label="Webhook subscriptions">
            {webhooksQuery.isLoading ? (
              <p className="text-sm text-muted-foreground">Loading webhooks…</p>
            ) : (
              webhooks.map((webhook) => (
                <article key={webhook.id} className="rounded-xl border border-border bg-background p-3">
                  <button
                    type="button"
                    aria-pressed={activeWebhookId === webhook.id}
                    onClick={() => setSelectedWebhookId(webhook.id)}
                    className="w-full text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  >
                    <span className="block break-all text-sm font-semibold">{webhook.url}</span>
                    <span className="mt-1 block text-xs text-muted-foreground">
                      {numberFormatter.format(webhook.eventTypes.length)} events · created {formatIsoDate(webhook.createdAtUtc)}
                    </span>
                  </button>
                  <div className="mt-3 flex items-center justify-between gap-2">
                    <span className="rounded-full bg-primary/10 px-2.5 py-1 text-xs font-semibold text-primary">
                      {webhook.isActive ? "Active" : "Paused"}
                    </span>
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={deleteWebhookMutation.isPending}
                      onClick={() => deleteWebhookMutation.mutate(webhook.id)}
                    >
                      Delete
                    </Button>
                  </div>
                </article>
              ))
            )}
          </div>

          <section className="overflow-hidden rounded-xl border border-border bg-background" aria-labelledby="deliveries-title">
            <header className="border-b border-border p-4">
              <h3 id="deliveries-title" className="text-sm font-semibold">
                Deliveries for {activeWebhook?.url ?? "selected webhook"}
              </h3>
            </header>
            <div className="overflow-x-auto">
              <table className="min-w-full text-left text-sm">
                <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
                  <tr>
                    <th className="px-4 py-3 font-semibold">Event</th>
                    <th className="px-4 py-3 font-semibold">Status code</th>
                    <th className="px-4 py-3 font-semibold">Success</th>
                    <th className="px-4 py-3 font-semibold">Attempt</th>
                    <th className="px-4 py-3 font-semibold">Time</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {deliveries.map((delivery) => (
                    <tr key={delivery.id}>
                      <td className="px-4 py-3">{delivery.eventType}</td>
                      <td className="px-4 py-3">{delivery.statusCode ?? "—"}</td>
                      <td className="px-4 py-3">
                        <span
                          className={cn(
                            "rounded-full px-2.5 py-1 text-xs font-semibold",
                            delivery.success
                              ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200"
                              : "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
                          )}
                        >
                          {delivery.success ? "Success" : "Failed"}
                        </span>
                      </td>
                      <td className="px-4 py-3">{numberFormatter.format(delivery.attempt)}</td>
                      <td className="px-4 py-3 text-muted-foreground">{formatIsoDateTime(delivery.occurredAtUtc)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {deliveriesQuery.isLoading ? (
                <p className="p-4 text-sm text-muted-foreground">Loading deliveries…</p>
              ) : null}
            </div>
          </section>
        </div>
      </section>

      <section className={cn(panelClassName, "p-4")} aria-labelledby="tokens-title">
        <div className="border-b border-border pb-4">
          <h2 id="tokens-title" className="text-lg font-semibold">
            Personal access tokens
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Tokens are scoped to the workspace, shown once, and then hidden. Copy the secret before dismissing the banner.
          </p>
        </div>

        {createdToken ? (
          <div role="alert" className="mt-4 rounded-xl border border-primary bg-primary/10 p-4 text-sm">
            <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
              <div>
                <h3 className="font-semibold text-primary">Copy this token now — it will only be shown once.</h3>
                <code className="mt-2 block break-all rounded-lg bg-background px-3 py-2 text-foreground">
                  {createdToken.token}
                </code>
                <p className="mt-2 text-xs text-muted-foreground">
                  {createdToken.name} · {createdToken.scopes.join(", ")}
                </p>
              </div>
              <div className="flex gap-2">
                <Button type="button" variant="secondary" size="sm" onClick={() => copyValue(createdToken.token, "Token copied. Store it securely now.")}>
                  Copy token
                </Button>
                <Button type="button" variant="ghost" size="sm" onClick={() => setCreatedToken(null)}>
                  Dismiss
                </Button>
              </div>
            </div>
          </div>
        ) : null}

        <form onSubmit={submitToken} className="mt-4 grid gap-4 lg:grid-cols-[1fr_14rem_auto]">
          <label className="grid gap-1 text-xs font-medium">
            Token name
            <input
              value={tokenName}
              onChange={(event) => setTokenName(event.target.value)}
              className={textInputClassName}
              placeholder="Workflow importer"
            />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Optional expiry
            <input
              type="date"
              value={tokenExpiryDate}
              onChange={(event) => setTokenExpiryDate(event.target.value)}
              className={textInputClassName}
            />
          </label>
          <Button type="submit" size="sm" className="self-end" disabled={createTokenMutation.isPending}>
            Create token
          </Button>
          <fieldset className="lg:col-span-3">
            <legend className="mb-2 text-xs font-medium">Scopes</legend>
            <div className="flex flex-wrap gap-2">
              {tokenScopes.map((scope) => (
                <label key={scope} className="flex items-center gap-2 rounded-full border border-border bg-background px-3 py-1.5 text-sm">
                  <input
                    type="checkbox"
                    checked={selectedScopes.includes(scope)}
                    onChange={() => setSelectedScopes((current) => toggleItem(current, scope))}
                    className="size-4 rounded border-border accent-[var(--primary)]"
                  />
                  {scope}
                </label>
              ))}
            </div>
          </fieldset>
        </form>

        <div className="mt-6 overflow-x-auto rounded-xl border border-border">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-4 py-3 font-semibold">Name</th>
                <th className="px-4 py-3 font-semibold">Scopes</th>
                <th className="px-4 py-3 font-semibold">Last used</th>
                <th className="px-4 py-3 font-semibold">Expires</th>
                <th className="px-4 py-3 font-semibold">Created</th>
                <th className="px-4 py-3 font-semibold">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {tokens.map((token) => (
                <tr key={token.id}>
                  <td className="px-4 py-3 font-semibold">{token.name}</td>
                  <td className="px-4 py-3">{token.scopes.join(", ")}</td>
                  <td className="px-4 py-3 text-muted-foreground">{formatIsoDateTime(token.lastUsedAtUtc)}</td>
                  <td className="px-4 py-3 text-muted-foreground">{formatIsoDateTime(token.expiresAtUtc)}</td>
                  <td className="px-4 py-3 text-muted-foreground">{formatIsoDateTime(token.createdAtUtc)}</td>
                  <td className="px-4 py-3">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={deleteTokenMutation.isPending}
                      onClick={() => deleteTokenMutation.mutate(token.id)}
                    >
                      Delete
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {tokensQuery.isLoading ? <p className="p-4 text-sm text-muted-foreground">Loading tokens…</p> : null}
        </div>
      </section>

      <section className={cn(panelClassName, "p-4")} aria-labelledby="oauth-apps-title">
        <div className="border-b border-border pb-4">
          <h2 id="oauth-apps-title" className="text-lg font-semibold">
            OAuth applications
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Let a third-party app request scoped access to this workspace via OAuth2. A scoped token can
            never do more than the scopes granted here.
          </p>
        </div>

        {createdApp ? (
          <div role="alert" className="mt-4 rounded-xl border border-primary bg-primary/10 p-4 text-sm">
            <h3 className="font-semibold text-primary">Copy the client secret now — it will only be shown once.</h3>
            <p className="mt-2 text-xs text-muted-foreground">Client ID</p>
            <code className="mt-1 block break-all rounded-lg bg-background px-3 py-2 text-foreground">{createdApp.clientId}</code>
            <p className="mt-2 text-xs text-muted-foreground">Client secret</p>
            <code className="mt-1 block break-all rounded-lg bg-background px-3 py-2 text-foreground">{createdApp.clientSecret}</code>
            <div className="mt-3 flex gap-2">
              <Button type="button" variant="secondary" size="sm" onClick={() => copyValue(createdApp.clientSecret, "Client secret copied.")}>
                Copy secret
              </Button>
              <Button type="button" variant="ghost" size="sm" onClick={() => setCreatedApp(null)}>
                Dismiss
              </Button>
            </div>
          </div>
        ) : null}

        <form onSubmit={submitApp} className="mt-4 grid gap-4 lg:grid-cols-[1fr_1fr_auto]">
          <label className="grid gap-1 text-xs font-medium">
            App name
            <input value={appName} onChange={(event) => setAppName(event.target.value)} className={textInputClassName} placeholder="Zapier connector" />
          </label>
          <label className="grid gap-1 text-xs font-medium">
            Redirect URI
            <input
              value={appRedirectUri}
              onChange={(event) => setAppRedirectUri(event.target.value)}
              className={textInputClassName}
              placeholder="https://example.com/oauth/callback"
            />
          </label>
          <Button type="submit" size="sm" className="self-end" disabled={createAppMutation.isPending}>
            Create application
          </Button>
          <fieldset className="lg:col-span-3">
            <legend className="mb-2 text-xs font-medium">Allowed scopes (the ceiling a token for this app can ever request)</legend>
            <div className="flex flex-wrap gap-2">
              {oauthScopeVocabulary.map((scope) => (
                <label key={scope} className="flex items-center gap-2 rounded-full border border-border bg-background px-3 py-1.5 text-sm">
                  <input
                    type="checkbox"
                    checked={appScopes.includes(scope)}
                    onChange={() => setAppScopes((current) => toggleItem(current, scope))}
                    className="size-4 rounded border-border accent-[var(--primary)]"
                  />
                  {scope}
                </label>
              ))}
            </div>
          </fieldset>
        </form>

        <div className="mt-6 overflow-x-auto rounded-xl border border-border">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-4 py-3 font-semibold">Name</th>
                <th className="px-4 py-3 font-semibold">Client ID</th>
                <th className="px-4 py-3 font-semibold">Allowed scopes</th>
                <th className="px-4 py-3 font-semibold">Status</th>
                <th className="px-4 py-3 font-semibold">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {apps.map((app) => (
                <tr key={app.id}>
                  <td className="px-4 py-3 font-semibold">{app.name}</td>
                  <td className="px-4 py-3 font-mono text-xs">{app.clientId}</td>
                  <td className="px-4 py-3">{app.allowedScopes.join(", ")}</td>
                  <td className="px-4 py-3">{app.isActive ? "Active" : "Revoked"}</td>
                  <td className="px-4 py-3">
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={!app.isActive || revokeAppMutation.isPending}
                      onClick={() => revokeAppMutation.mutate(app.id)}
                    >
                      Revoke
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {appsQuery.isLoading ? <p className="p-4 text-sm text-muted-foreground">Loading applications…</p> : null}
        </div>
      </section>

      <section className={cn(panelClassName, "p-4")} aria-labelledby="providers-title">
        <div className="border-b border-border pb-4">
          <h2 id="providers-title" className="text-lg font-semibold">
            Third-party integrations
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Slack and GitHub call the real API when configured and enabled. Every other provider below is
            settings-only scaffolding — not yet wired to a live call.
          </p>
        </div>

        <div className="mt-4 grid gap-4 md:grid-cols-2">
          {providers.map((settings) => {
            const draft = providerDraft(settings);
            return (
              <article key={settings.provider} className="rounded-xl border border-border bg-background p-4">
                <div className="flex items-center justify-between gap-2">
                  <h3 className="text-sm font-semibold">{settings.provider}</h3>
                  <span
                    className={cn(
                      "rounded-full px-2.5 py-1 text-xs font-semibold",
                      settings.hasRealImplementation
                        ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200"
                        : "bg-muted text-muted-foreground",
                    )}
                  >
                    {settings.hasRealImplementation ? "Live call" : "Scaffolding only"}
                  </span>
                </div>
                <label className="mt-3 grid gap-1 text-xs font-medium">
                  Config (JSON — e.g. {"{\"owner\":\"acme\",\"repo\":\"app\"}"})
                  <textarea
                    value={draft.configJson}
                    onChange={(event) => setProviderDraft(settings.provider, { configJson: event.target.value })}
                    className="min-h-16 rounded-lg border border-border bg-background px-3 py-2 font-mono text-xs"
                  />
                </label>
                <label className="mt-2 grid gap-1 text-xs font-medium">
                  Secret {settings.secretHint ? `(current: ${settings.secretHint})` : "(not set)"}
                  <input
                    type="password"
                    value={draft.secret}
                    onChange={(event) => setProviderDraft(settings.provider, { secret: event.target.value })}
                    className={textInputClassName}
                    placeholder="Leave blank to keep the stored value"
                  />
                </label>
                <div className="mt-3 flex items-center justify-between">
                  <label className="flex items-center gap-2 text-xs font-medium">
                    <input
                      type="checkbox"
                      checked={settings.isEnabled}
                      onChange={(event) =>
                        updateProviderMutation.mutate({
                          provider: settings.provider,
                          configJson: draft.configJson,
                          secret: draft.secret,
                          isEnabled: event.target.checked,
                        })
                      }
                      className="size-4 rounded border-border accent-[var(--primary)]"
                    />
                    Enabled
                  </label>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    disabled={updateProviderMutation.isPending}
                    onClick={() =>
                      updateProviderMutation.mutate({
                        provider: settings.provider,
                        configJson: draft.configJson,
                        secret: draft.secret,
                        isEnabled: settings.isEnabled,
                      })
                    }
                  >
                    Save
                  </Button>
                </div>
              </article>
            );
          })}
          {providersQuery.isLoading ? <p className="text-sm text-muted-foreground">Loading providers…</p> : null}
        </div>
      </section>
    </section>
  );
}
