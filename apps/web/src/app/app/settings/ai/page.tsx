"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { FormEvent } from "react";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import {
  getAiProviderSettings,
  testAiProviderSettings,
  updateAiFeaturesEnabled,
  updateAiProviderSettings,
} from "@/lib/ai/client";
import { aiKeys } from "@/lib/ai/queries";
import type { AiProviderSettings, UpdateAiProviderSettingsInput } from "@/lib/ai/types";
import { cn } from "@/lib/utils";

const panelClassName = "rounded-[var(--radius)] border border-border bg-card shadow-sm";
const inputClassName =
  "h-10 rounded-lg border border-border bg-background px-3 text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50";

const emptySettings: AiProviderSettings = {
  baseUrl: "",
  model: "",
  apiKeyMask: "",
  isEnabled: false,
  aiFeaturesEnabled: true,
  creditLimit: null,
};

export default function AiProviderSettingsPage() {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<UpdateAiProviderSettingsInput | null>(null);
  const [statusMessage, setStatusMessage] = useState("");

  const settingsQuery = useQuery({ queryKey: aiKeys.settings(), queryFn: getAiProviderSettings });
  const saved = settingsQuery.data ?? emptySettings;
  const current: UpdateAiProviderSettingsInput = draft ?? {
    baseUrl: saved.baseUrl,
    model: saved.model,
    apiKey: "",
    isEnabled: saved.isEnabled,
    creditLimit: saved.creditLimit ?? null,
  };

  const saveMutation = useMutation({
    mutationFn: updateAiProviderSettings,
    onSuccess: (settings) => {
      // The key is write-only: clear the input so the stored one is kept on the next save.
      setDraft({
        baseUrl: settings.baseUrl,
        model: settings.model,
        apiKey: "",
        isEnabled: settings.isEnabled,
        creditLimit: settings.creditLimit ?? null,
      });
      setStatusMessage("AI provider settings saved.");
      void queryClient.invalidateQueries({ queryKey: aiKeys.settings() });
    },
    onError: (error: Error) => setStatusMessage(error.message),
  });

  const testMutation = useMutation({
    mutationFn: testAiProviderSettings,
    onError: (error: Error) => setStatusMessage(error.message),
  });

  const featuresMutation = useMutation({
    mutationFn: updateAiFeaturesEnabled,
    onSuccess: () => {
      setStatusMessage("AI features setting saved.");
      void queryClient.invalidateQueries({ queryKey: aiKeys.settings() });
      void queryClient.invalidateQueries({ queryKey: aiKeys.featureStatus() });
    },
    onError: (error: Error) => setStatusMessage(error.message),
  });

  function update(patch: Partial<UpdateAiProviderSettingsInput>) {
    setDraft({ ...current, ...patch });
    setStatusMessage("");
    testMutation.reset();
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    saveMutation.mutate(current);
  }

  const testResult = testMutation.data;

  return (
    <section aria-labelledby="ai-settings-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Settings · AI provider</p>
        <h1 id="ai-settings-title" className="mt-2 text-3xl font-semibold tracking-tight">
          AI Provider
        </h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-muted-foreground">
          Point AI assist at your own LiteLLM or OpenAI-compatible endpoint. While this is off, Planvexa uses
          its built-in offline assistant instead.
        </p>
      </div>

      {statusMessage ? (
        <p role="status" className="rounded-lg bg-primary/10 px-4 py-3 text-sm font-medium text-primary">
          {statusMessage}
        </p>
      ) : null}

      {!settingsQuery.isLoading ? (
        <section className={cn(panelClassName, "p-5")} aria-labelledby="ai-features-title">
          <label className="flex items-start gap-3">
            <input
              id="ai-features-enabled"
              type="checkbox"
              checked={saved.aiFeaturesEnabled}
              disabled={featuresMutation.isPending}
              onChange={(event) => featuresMutation.mutate(event.target.checked)}
              className="mt-1 size-4 rounded border-border accent-[var(--primary)] disabled:cursor-not-allowed disabled:opacity-50"
            />
            <span>
              <span id="ai-features-title" className="block text-sm font-semibold">
                Allow AI in this workspace
              </span>
              <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                Master switch. When off, every AI action — summaries, subtasks, priority and risk
                suggestions, duplicate detection, workspace Q&amp;A and semantic search — is blocked for
                everyone in this workspace, and the AI Assist entry points are hidden.
              </span>
            </span>
          </label>
        </section>
      ) : null}

      {settingsQuery.isLoading ? (
        <section className={cn(panelClassName, "p-6 text-sm text-muted-foreground")}>Loading AI provider settings…</section>
      ) : (
        <form onSubmit={submit} className={cn(panelClassName, "space-y-6 p-5")}>
          <fieldset className="grid gap-5 lg:grid-cols-2">
            <legend className="sr-only">Endpoint</legend>
            <label htmlFor="ai-base-url" className="grid gap-2 text-sm font-medium">
              Base URL
              <input
                id="ai-base-url"
                type="url"
                inputMode="url"
                placeholder="http://localhost:4000"
                value={current.baseUrl}
                onChange={(event) => update({ baseUrl: event.target.value })}
                className={inputClassName}
                aria-describedby="ai-base-url-help"
              />
              <span id="ai-base-url-help" className="text-xs leading-5 text-muted-foreground">
                Requests go to <code>{`${current.baseUrl.replace(/\/$/, "") || "<base URL>"}/chat/completions`}</code>.
              </span>
            </label>

            <label htmlFor="ai-model" className="grid gap-2 text-sm font-medium">
              Model
              <input
                id="ai-model"
                type="text"
                placeholder="gpt-4o-mini"
                value={current.model}
                onChange={(event) => update({ model: event.target.value })}
                className={inputClassName}
                aria-describedby="ai-model-help"
              />
              <span id="ai-model-help" className="text-xs leading-5 text-muted-foreground">
                The model name as your LiteLLM deployment exposes it.
              </span>
            </label>

            <label htmlFor="ai-api-key" className="grid gap-2 text-sm font-medium lg:col-span-2">
              API key
              <input
                id="ai-api-key"
                type="password"
                autoComplete="off"
                placeholder={saved.apiKeyMask || "Leave blank if your endpoint needs no key"}
                value={current.apiKey ?? ""}
                onChange={(event) => update({ apiKey: event.target.value })}
                className={inputClassName}
                aria-describedby="ai-api-key-help"
              />
              <span id="ai-api-key-help" className="text-xs leading-5 text-muted-foreground">
                Stored encrypted and never shown again. Leave blank to keep the current key
                {saved.apiKeyMask ? ` (${saved.apiKeyMask})` : ""}.
              </span>
            </label>
          </fieldset>

          <fieldset className="space-y-3">
            <legend className="text-lg font-semibold">Routing</legend>
            <label className="flex items-start gap-3 rounded-xl border border-border bg-background p-4 focus-within:outline focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring">
              <input
                type="checkbox"
                checked={current.isEnabled}
                onChange={(event) => update({ isEnabled: event.target.checked })}
                className="mt-1 size-4 rounded border-border accent-[var(--primary)]"
              />
              <span>
                <span className="block text-sm font-semibold">Use this provider for AI assist</span>
                <span className="mt-1 block text-xs leading-5 text-muted-foreground">
                  When off, summaries, subtask suggestions and priority hints come from the built-in offline
                  assistant. Provider errors are reported rather than silently falling back.
                </span>
              </span>
            </label>

            <label htmlFor="ai-credit-limit" className="grid gap-2 text-sm font-medium">
              Monthly credit limit (tokens)
              <input
                id="ai-credit-limit"
                type="number"
                min={0}
                inputMode="numeric"
                placeholder="Unlimited"
                value={current.creditLimit ?? ""}
                onChange={(event) =>
                  update({ creditLimit: event.target.value === "" ? null : Number(event.target.value) })
                }
                className={inputClassName}
                aria-describedby="ai-credit-limit-help"
              />
              <span id="ai-credit-limit-help" className="text-xs leading-5 text-muted-foreground">
                Caps estimated tokens spent through this provider per calendar month. Once reached, real AI
                calls are rejected until next month; the offline assistant keeps working. Leave blank for no limit.
              </span>
            </label>
          </fieldset>

          {testResult ? (
            <p
              role="status"
              className={cn(
                "rounded-xl border p-4 text-sm",
                testResult.ok
                  ? "border-emerald-300 bg-emerald-50 text-emerald-900 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-100"
                  : "border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100",
              )}
            >
              {testResult.message}
            </p>
          ) : null}

          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-5">
            <p className="text-sm text-muted-foreground">Applies to every workspace in this account.</p>
            <div className="flex flex-wrap gap-3">
              <Button
                type="button"
                variant="secondary"
                disabled={testMutation.isPending}
                onClick={() => {
                  setStatusMessage("");
                  testMutation.mutate(current);
                }}
              >
                {testMutation.isPending ? "Testing…" : "Test connection"}
              </Button>
              <Button type="submit" disabled={saveMutation.isPending}>
                Save AI provider
              </Button>
            </div>
          </div>
        </form>
      )}
    </section>
  );
}
