"use client";

import Link from "next/link";
import { useMemo } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getDigestPreference, getPreferences, setDigestPreference, setPreference } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import {
  NOTIFICATION_EVENT_LABELS,
  NOTIFICATION_EVENT_TYPES,
  type DigestFrequency,
  type NotificationPreference,
  type PreferencePatch,
} from "@/lib/collab/types";

const DIGEST_OPTIONS: { value: DigestFrequency; label: string }[] = [
  { value: "Off", label: "Off" },
  { value: "Daily", label: "Daily" },
  { value: "Weekly", label: "Weekly" },
];

export default function NotificationPreferencesPage() {
  const queryClient = useQueryClient();
  const preferencesQuery = useQuery({
    queryKey: collabKeys.preferences(),
    queryFn: getPreferences,
  });
  const digestQuery = useQuery({
    queryKey: collabKeys.digestPreference(),
    queryFn: getDigestPreference,
  });
  const preferenceMutation = useMutation({
    mutationFn: ({ eventType, patch }: { eventType: string; patch: PreferencePatch }) =>
      setPreference(eventType, patch),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: collabKeys.preferences() });
    },
  });
  const digestMutation = useMutation({
    mutationFn: setDigestPreference,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: collabKeys.digestPreference() });
    },
  });

  // The API stores preferences sparsely — an absent row means inbox/email are on and push is off — so
  // the known event types are merged in with those defaults.
  const preferences = useMemo<NotificationPreference[]>(() => {
    const stored = new Map((preferencesQuery.data ?? []).map((item) => [item.eventType, item]));
    const known = NOTIFICATION_EVENT_TYPES.map(
      (eventType) => stored.get(eventType) ?? { eventType, inbox: true, email: true, push: false },
    );
    const extra = (preferencesQuery.data ?? []).filter(
      (item) => !NOTIFICATION_EVENT_TYPES.includes(item.eventType as (typeof NOTIFICATION_EVENT_TYPES)[number]),
    );
    return [...known, ...extra];
  }, [preferencesQuery.data]);

  function updatePreference(preference: NotificationPreference, patch: Partial<PreferencePatch>) {
    preferenceMutation.mutate({
      eventType: preference.eventType,
      patch: {
        inbox: patch.inbox ?? preference.inbox,
        email: patch.email ?? preference.email,
        push: patch.push ?? preference.push,
      },
    });
  }

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-wide text-muted-foreground">
            Notification settings
          </p>
          <h1 className="text-3xl font-semibold tracking-tight">Preferences</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            Choose which collaboration events should appear in the inbox or be emailed.
          </p>
        </div>
        <Link
          href="/app/notifications"
          className="inline-flex h-11 items-center rounded-lg border border-border bg-card px-4 text-sm font-medium hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          Back to inbox
        </Link>
      </div>

      <div className="overflow-hidden rounded-2xl border border-border bg-card shadow-sm">
        <div className="grid grid-cols-[1fr_auto_auto_auto] gap-4 border-b border-border px-4 py-3 text-sm font-semibold text-muted-foreground">
          <span>Event</span>
          <span>Inbox</span>
          <span>Email</span>
          <span>Push</span>
        </div>

        {preferencesQuery.isLoading ? (
          <p className="p-4 text-sm text-muted-foreground">Loading preferences…</p>
        ) : (
          <ul className="divide-y divide-border">
            {preferences.map((preference) => {
              const label = NOTIFICATION_EVENT_LABELS[preference.eventType] ?? preference.eventType;

              return (
                <li
                  key={preference.eventType}
                  className="grid grid-cols-[1fr_auto_auto_auto] items-center gap-4 px-4 py-4"
                >
                  <div>
                    <h2 className="text-sm font-semibold">{label}</h2>
                    <p className="text-xs text-muted-foreground">{preference.eventType}</p>
                  </div>
                  <label className="inline-flex items-center gap-2 text-sm">
                    <span className="sr-only">Inbox for {label}</span>
                    <input
                      type="checkbox"
                      checked={preference.inbox}
                      className="size-4 accent-[var(--primary)]"
                      disabled={preferenceMutation.isPending}
                      onChange={(event) => updatePreference(preference, { inbox: event.currentTarget.checked })}
                    />
                  </label>
                  <label className="inline-flex items-center gap-2 text-sm">
                    <span className="sr-only">Email for {label}</span>
                    <input
                      type="checkbox"
                      checked={preference.email}
                      className="size-4 accent-[var(--primary)]"
                      disabled={preferenceMutation.isPending}
                      onChange={(event) => updatePreference(preference, { email: event.currentTarget.checked })}
                    />
                  </label>
                  <label className="inline-flex items-center gap-2 text-sm">
                    <span className="sr-only">Push for {label}</span>
                    <input
                      type="checkbox"
                      checked={preference.push}
                      className="size-4 accent-[var(--primary)]"
                      disabled={preferenceMutation.isPending}
                      onChange={(event) => updatePreference(preference, { push: event.currentTarget.checked })}
                    />
                  </label>
                </li>
              );
            })}
          </ul>
        )}
      </div>

      <div className="rounded-2xl border border-border bg-card p-4 shadow-sm">
        <h2 className="text-sm font-semibold">Activity digest</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          A summary email of your unread inbox, sent on a schedule instead of one-by-one.
        </p>
        <label className="mt-3 grid max-w-xs gap-2 text-sm font-medium">
          Frequency
          <select
            value={digestQuery.data?.frequency ?? "Off"}
            disabled={digestMutation.isPending || digestQuery.isLoading}
            className="h-10 rounded-lg border border-border bg-background px-3 text-sm font-normal focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            onChange={(event) => digestMutation.mutate(event.currentTarget.value as DigestFrequency)}
          >
            {DIGEST_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>
      </div>
    </section>
  );
}
