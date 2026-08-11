/**
 * Browser Web Push subscription flow (frontend half of the push gap; see
 * `apps/api/Planvexa.Api/Notifications/LoggingPushSender.cs`'s doc comment for the backend half that
 * remains: RFC 8291 payload encryption + delivery, still log-only via `LoggingPushSender`).
 */
import { getVapidPublicKey, registerDevice } from "@/lib/ai/client";
import type { Device } from "@/lib/ai/types";

function urlBase64ToUint8Array(base64Url: string): Uint8Array {
  const padding = "=".repeat((4 - (base64Url.length % 4)) % 4);
  const base64 = (base64Url + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = atob(base64);
  return Uint8Array.from(raw, (char) => char.charCodeAt(0));
}

function arrayBufferToBase64Url(buffer: ArrayBuffer | null): string {
  if (!buffer) return "";
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export function isPushSupported() {
  return typeof window !== "undefined" && "serviceWorker" in navigator && "PushManager" in window;
}

/** Requests Notification permission, subscribes via the service worker's PushManager, and registers
 * the subscription with the backend (`POST /mobile/devices`) so `IPushDeviceDirectory` sees this user
 * as push-capable. Returns the registered `Device`, or throws if permission was denied or the browser
 * doesn't support Push. */
export async function subscribeToPush(): Promise<Device> {
  if (!isPushSupported()) {
    throw new Error("Push notifications are not supported in this browser.");
  }

  const permission = await Notification.requestPermission();
  if (permission !== "granted") {
    throw new Error("Notification permission was not granted.");
  }

  const registration = await navigator.serviceWorker.ready;
  let subscription = await registration.pushManager.getSubscription();
  if (!subscription) {
    const vapidPublicKey = await getVapidPublicKey();
    subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(vapidPublicKey) as BufferSource,
    });
  }

  const json = subscription.toJSON();
  return registerDevice({
    platform: "Web",
    // The endpoint is a stable, unique-per-subscription URL -- reusing it as the "push token" means
    // re-subscribing (e.g. after granting permission again) hits the existing device-registration
    // dedupe (DeviceService.RegisterAsync matches on the token's hash) instead of creating a new row
    // every time, unlike the previous crypto.randomUUID() placeholder this replaces.
    pushToken: subscription.endpoint,
    endpoint: subscription.endpoint,
    p256dh: json.keys?.p256dh ?? arrayBufferToBase64Url(subscription.getKey("p256dh")),
    auth: json.keys?.auth ?? arrayBufferToBase64Url(subscription.getKey("auth")),
  });
}
