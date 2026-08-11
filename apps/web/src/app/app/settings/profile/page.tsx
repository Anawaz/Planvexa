"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { ChangeEvent, FormEvent } from "react";
import { useMemo, useRef, useState } from "react";
import { apiClient } from "@/lib/api-client";
import { Avatar } from "@/components/ui/Avatar";
import { Button } from "@/components/ui/Button";
import { useAppContext } from "@/lib/app-context/AppContext";
import { cn } from "@/lib/utils";

type CurrentUser = {
  userId: string;
  email: string;
  displayName: string;
  avatarUrl?: string | null;
  timezone?: string | null;
  locale?: string | null;
};

// Common BCP 47 locales — a curated shortlist, not an exhaustive registry (there is no canonical
// "all locales" list worth enumerating). The field still accepts any tag typed in; the datalist is
// just a convenience, same free-text-plus-suggestions pattern as the timezone field below.
const COMMON_LOCALES = [
  "en-US", "en-GB", "de-DE", "fr-FR", "es-ES", "pt-BR", "it-IT", "nl-NL",
  "sv-SE", "pl-PL", "ja-JP", "ko-KR", "zh-CN", "zh-TW", "hi-IN", "ar-SA",
];

const panelClassName = "rounded-[var(--radius)] border border-border bg-card shadow-sm";
const inputClassName =
  "h-10 rounded-lg border border-border bg-background px-3 text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50";

export default function ProfilePage() {
  const { currentUser } = useAppContext();

  if (!currentUser) {
    return <p className="text-sm text-muted-foreground">Loading your profile…</p>;
  }

  // Keyed by userId so the form's local draft state resets if the signed-in account ever changes,
  // without needing an effect to sync an external value into state.
  return <ProfileForm key={currentUser.userId} user={currentUser} />;
}

function initials(name: string) {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0])
    .join("")
    .toUpperCase();
}

function ProfileForm({ user }: { user: CurrentUser }) {
  const queryClient = useQueryClient();
  const [displayName, setDisplayName] = useState(user.displayName);
  const [timezone, setTimezone] = useState(user.timezone ?? "");
  const [locale, setLocale] = useState(user.locale ?? "");
  const [statusMessage, setStatusMessage] = useState("");
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Intl.supportedValuesOf is the runtime's own IANA timezone database — no need to ship/maintain a
  // separate list.
  const timezoneOptions = useMemo(() => {
    try {
      return Intl.supportedValuesOf("timeZone");
    } catch {
      return [];
    }
  }, []);

  const saveMutation = useMutation({
    mutationFn: (input: { displayName: string; timezone: string; locale: string }) =>
      apiClient.patch<CurrentUser>("/users/me", {
        displayName: input.displayName,
        timezone: input.timezone.trim() || null,
        locale: input.locale.trim() || null,
      }),
    onSuccess: (updated) => {
      setDisplayName(updated.displayName);
      setTimezone(updated.timezone ?? "");
      setLocale(updated.locale ?? "");
      setStatusMessage("Profile updated.");
      void queryClient.invalidateQueries({ queryKey: ["user", "me"] });
    },
    onError: (error: unknown) => {
      setStatusMessage(error instanceof Error ? error.message : "Could not update your profile.");
    },
  });

  const avatarMutation = useMutation({
    mutationFn: (file: File) => {
      const body = new FormData();
      body.append("file", file);
      return apiClient.post<CurrentUser, FormData>("/users/me/avatar", body);
    },
    onSuccess: () => {
      setStatusMessage("Avatar updated.");
      void queryClient.invalidateQueries({ queryKey: ["user", "me"] });
    },
    onError: (error: unknown) => {
      setStatusMessage(error instanceof Error ? error.message : "Could not upload your avatar.");
    },
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setStatusMessage("");
    saveMutation.mutate({ displayName: displayName.trim(), timezone, locale });
  }

  function selectAvatar(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (file) {
      setStatusMessage("");
      avatarMutation.mutate(file);
    }
  }

  return (
    <section aria-labelledby="profile-title" className="max-w-xl space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Your account</p>
        <h1 id="profile-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Profile
        </h1>
        <p className="mt-3 text-sm leading-6 text-muted-foreground">
          This is your personal account, not a Workspace setting — it applies everywhere you sign in.
        </p>
      </div>

      {statusMessage ? (
        <p role="status" className="rounded-lg bg-primary/10 px-4 py-3 text-sm font-medium text-primary">
          {statusMessage}
        </p>
      ) : null}

      <div className={cn(panelClassName, "flex items-center gap-4 p-5")}>
        <Avatar
          avatarUrl={user.avatarUrl}
          initials={initials(displayName)}
          className="grid size-16 shrink-0 place-items-center rounded-full bg-primary text-lg font-semibold text-primary-foreground"
        />
        <div className="space-y-1">
          <input
            ref={fileInputRef}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={selectAvatar}
          />
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={avatarMutation.isPending}
            onClick={() => fileInputRef.current?.click()}
          >
            {avatarMutation.isPending ? "Uploading…" : "Change avatar"}
          </Button>
          <p className="text-xs text-muted-foreground">JPG, PNG, GIF, or WebP. Up to 5 MB.</p>
        </div>
      </div>

      <form onSubmit={submit} className={cn(panelClassName, "space-y-4 p-5")}>
        <label htmlFor="profile-email" className="grid gap-2 text-sm font-medium">
          Email
          <input
            id="profile-email"
            value={user.email}
            disabled
            className={inputClassName}
          />
        </label>
        <p className="text-xs text-muted-foreground">
          Managed by your sign-in provider and can&apos;t be changed here.
        </p>

        <label htmlFor="profile-display-name" className="grid gap-2 text-sm font-medium">
          Display name
          <input
            id="profile-display-name"
            value={displayName}
            onChange={(event) => setDisplayName(event.target.value)}
            maxLength={200}
            required
            className={inputClassName}
          />
        </label>

        <label htmlFor="profile-timezone" className="grid gap-2 text-sm font-medium">
          Timezone
          <input
            id="profile-timezone"
            list="profile-timezone-options"
            value={timezone}
            onChange={(event) => setTimezone(event.target.value)}
            placeholder="Use browser default"
            className={inputClassName}
          />
          <datalist id="profile-timezone-options">
            {timezoneOptions.map((zone) => (
              <option key={zone} value={zone} />
            ))}
          </datalist>
        </label>
        <p className="-mt-2 text-xs text-muted-foreground">
          IANA timezone id (e.g. &quot;America/New_York&quot;). Leave blank to use your browser&apos;s timezone.
        </p>

        <label htmlFor="profile-locale" className="grid gap-2 text-sm font-medium">
          Language &amp; date format
          <input
            id="profile-locale"
            list="profile-locale-options"
            value={locale}
            onChange={(event) => setLocale(event.target.value)}
            placeholder="Use browser default"
            className={inputClassName}
          />
          <datalist id="profile-locale-options">
            {COMMON_LOCALES.map((tag) => (
              <option key={tag} value={tag} />
            ))}
          </datalist>
        </label>
        <p className="-mt-2 text-xs text-muted-foreground">
          BCP 47 language tag (e.g. &quot;en-US&quot;). Controls date and number formatting. Leave blank to use
          your browser&apos;s language.
        </p>

        <Button type="submit" disabled={saveMutation.isPending || displayName.trim().length === 0}>
          Save changes
        </Button>
      </form>
    </section>
  );
}
