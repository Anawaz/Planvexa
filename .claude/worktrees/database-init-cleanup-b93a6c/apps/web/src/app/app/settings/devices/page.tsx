"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { FormEvent } from "react";
import { useState } from "react";
import { Button } from "@/components/ui/Button";
import { listDevices, registerDevice, unregisterDevice } from "@/lib/ai/client";
import { aiKeys } from "@/lib/ai/queries";
import type { Device, DevicePlatform } from "@/lib/ai/types";
import { isPushSupported, subscribeToPush } from "@/lib/push/subscribe";
import { cn } from "@/lib/utils";

const numberFormatter = new Intl.NumberFormat("en");
const dateTimeFormatter = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });
const panelClassName = "rounded-[var(--radius)] border border-border bg-card shadow-sm";
const textInputClassName =
  "h-10 rounded-lg border border-border bg-background px-3 text-sm outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:cursor-not-allowed disabled:opacity-50";

const platformOptions: Array<{ value: DevicePlatform; label: string; icon: string }> = [
  { value: "Ios", label: "iOS", icon: "◐" },
  { value: "Android", label: "Android", icon: "◆" },
  { value: "Web", label: "Web", icon: "◎" },
];

function platformLabel(platform: DevicePlatform) {
  return platformOptions.find((option) => option.value === platform)?.label ?? platform;
}

function platformIcon(platform: DevicePlatform) {
  return platformOptions.find((option) => option.value === platform)?.icon ?? "•";
}

function FormattedDateTime({ value }: { value: string }) {
  return <time dateTime={value}>{dateTimeFormatter.format(new Date(value))}</time>;
}

function DeviceCard({
  device,
  onUnregister,
  disabled,
}: {
  device: Device;
  onUnregister: (id: string) => void;
  disabled: boolean;
}) {
  return (
    <article className="rounded-xl border border-border bg-background p-4">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex gap-3">
          <span
            aria-hidden="true"
            className="flex size-10 items-center justify-center rounded-full bg-primary/10 text-lg font-semibold text-primary"
          >
            {platformIcon(device.platform)}
          </span>
          <div>
            <h3 className="text-sm font-semibold">{platformLabel(device.platform)} device</h3>
            <p className="mt-1 text-sm text-muted-foreground">App version: {device.appVersion ?? "Not reported"}</p>
            <p className="mt-1 text-xs text-muted-foreground">
              Registered <FormattedDateTime value={device.createdAtUtc} />
            </p>
          </div>
        </div>
        <Button type="button" variant="outline" size="sm" disabled={disabled} onClick={() => onUnregister(device.id)}>
          Unregister
        </Button>
      </div>
      <p className="mt-4 rounded-lg bg-muted px-3 py-2 text-xs text-muted-foreground">
        Last seen <FormattedDateTime value={device.lastSeenAtUtc} />
      </p>
    </article>
  );
}

export default function DevicesPage() {
  const queryClient = useQueryClient();
  const [platform, setPlatform] = useState<DevicePlatform>("Ios");
  const [appVersion, setAppVersion] = useState("");
  const [statusMessage, setStatusMessage] = useState("");

  const devicesQuery = useQuery({ queryKey: aiKeys.devices(), queryFn: listDevices });
  const registerMutation = useMutation({
    mutationFn: registerDevice,
    onSuccess: (device) => {
      setAppVersion("");
      setStatusMessage(`${platformLabel(device.platform)} device registered.`);
      void queryClient.invalidateQueries({ queryKey: aiKeys.devicesRoot() });
    },
  });
  const unregisterMutation = useMutation({
    mutationFn: unregisterDevice,
    onSuccess: () => {
      setStatusMessage("Device unregistered.");
      void queryClient.invalidateQueries({ queryKey: aiKeys.devicesRoot() });
    },
  });
  const subscribePushMutation = useMutation({
    mutationFn: subscribeToPush,
    onSuccess: (device) => {
      setStatusMessage("Push notifications enabled for this browser.");
      void queryClient.invalidateQueries({ queryKey: aiKeys.devicesRoot() });
      void device;
    },
    onError: (error: unknown) => {
      setStatusMessage(error instanceof Error ? error.message : "Could not enable push notifications.");
    },
  });

  function submitDevice(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    // Manual/testing registration for non-browser platforms, which have no real subscription flow
    // here (no APNs/FCM credentials in this environment -- see LoggingPushSender.cs). A real browser
    // registration should use "Enable push in this browser" above instead, which supplies a genuine
    // PushSubscription (endpoint/p256dh/auth) rather than a random placeholder token.
    registerMutation.mutate({
      platform,
      pushToken: crypto.randomUUID(),
      appVersion: appVersion.trim() || null,
    });
  }

  const devices = devicesQuery.data ?? [];

  return (
    <section aria-labelledby="devices-title" className="space-y-6">
      <div>
        <p className="text-sm font-medium text-primary">Mobile access</p>
        <h1 id="devices-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Devices
        </h1>
        <p className="mt-3 max-w-3xl text-sm leading-6 text-muted-foreground">
          Register and remove mobile or browser clients for push delivery.
        </p>
      </div>

      <aside className="rounded-[var(--radius)] border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800 dark:border-blue-900 dark:bg-blue-950 dark:text-blue-200">
        Push tokens are stored hashed and never displayed.
      </aside>

      {isPushSupported() ? (
        <div className={cn(panelClassName, "flex flex-wrap items-center justify-between gap-3 p-5")}>
          <div>
            <h2 className="text-sm font-semibold">Push notifications in this browser</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Subscribes this browser to real push delivery via a genuine PushSubscription, not a placeholder token.
            </p>
          </div>
          <Button type="button" disabled={subscribePushMutation.isPending} onClick={() => subscribePushMutation.mutate()}>
            Enable push in this browser
          </Button>
        </div>
      ) : null}

      {statusMessage ? (
        <p role="status" className="rounded-lg bg-primary/10 px-4 py-3 text-sm font-medium text-primary">
          {statusMessage}
        </p>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[22rem_minmax(0,1fr)]">
        <form onSubmit={submitDevice} className={cn(panelClassName, "h-fit space-y-4 p-5")} aria-labelledby="register-device-title">
          <div>
            <h2 id="register-device-title" className="text-lg font-semibold">
              Register this device
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">Add the current client to workspace mobile access records.</p>
          </div>

          <label htmlFor="device-platform" className="grid gap-2 text-sm font-medium">
            Platform
            <select
              id="device-platform"
              value={platform}
              onChange={(event) => setPlatform(event.target.value as DevicePlatform)}
              className={textInputClassName}
            >
              {platformOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>

          <label htmlFor="device-app-version" className="grid gap-2 text-sm font-medium">
            App version <span className="text-xs font-normal text-muted-foreground">Optional</span>
            <input
              id="device-app-version"
              value={appVersion}
              onChange={(event) => setAppVersion(event.target.value)}
              className={textInputClassName}
              placeholder="8.0.1"
              autoComplete="off"
            />
          </label>

          <Button type="submit" disabled={registerMutation.isPending}>
            Register device
          </Button>
        </form>

        <section className={cn(panelClassName, "overflow-hidden")} aria-labelledby="registered-devices-title">
          <div className="flex flex-col gap-2 border-b border-border p-5 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <h2 id="registered-devices-title" className="text-lg font-semibold">
                Registered devices
              </h2>
              <p className="mt-1 text-sm text-muted-foreground">
                {devicesQuery.isLoading ? "Loading devices…" : `${numberFormatter.format(devices.length)} active registrations`}
              </p>
            </div>
          </div>

          {devicesQuery.isLoading ? (
            <p className="p-5 text-sm text-muted-foreground">Loading registered devices…</p>
          ) : devices.length === 0 ? (
            <p className="p-5 text-sm text-muted-foreground">No registered devices yet.</p>
          ) : (
            <div className="grid gap-4 p-5 lg:grid-cols-2">
              {devices.map((device) => (
                <DeviceCard
                  key={device.id}
                  device={device}
                  disabled={unregisterMutation.isPending}
                  onUnregister={(id) => unregisterMutation.mutate(id)}
                />
              ))}
            </div>
          )}
        </section>
      </div>
    </section>
  );
}
