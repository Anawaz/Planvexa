"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import { listNotifications, unreadCount } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import { NOTIFICATION_EVENT_LABELS, type Notification } from "@/lib/collab/types";
import { useMemberDirectory } from "@/lib/members";
import { cn } from "@/lib/utils";
import { useNotificationMutations } from "./useNotificationMutations";

export function payloadText(notification: Notification, key: string) {
  return notification.payload?.[key] ?? "";
}

export function notificationTitle(notification: Notification) {
  return (
    payloadText(notification, "taskTitle") ||
    NOTIFICATION_EVENT_LABELS[notification.eventType] ||
    notification.eventType
  );
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

export function NotificationBell() {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  // 30s poll is the realtime fallback until the SignalR client lands.
  const countQuery = useQuery({
    queryKey: collabKeys.unreadCount(),
    queryFn: unreadCount,
    refetchInterval: 30_000,
  });
  const notificationsQuery = useQuery({
    queryKey: collabKeys.notifications({}),
    queryFn: () => listNotifications({}),
  });
  const directory = useMemberDirectory();
  const { markReadMutation, markAllReadMutation } = useNotificationMutations();
  const notifications = (notificationsQuery.data ?? []).slice(0, 6);
  const unread = countQuery.data ?? 0;

  useEffect(() => {
    if (!open) {
      return;
    }

    function handlePointerDown(event: MouseEvent) {
      if (!containerRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        event.preventDefault();
        setOpen(false);
        buttonRef.current?.focus();
      }
    }

    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [open]);

  return (
    <div ref={containerRef} className="relative">
      <Button
        ref={buttonRef}
        type="button"
        variant="outline"
        size="sm"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={`Notifications${unread > 0 ? `, ${unread} unread` : ""}`}
        className="relative px-3"
        onClick={() => setOpen((current) => !current)}
      >
        <span aria-hidden="true">🔔</span>
        {unread > 0 ? (
          <span className="absolute -right-1 -top-1 min-w-5 rounded-full bg-red-600 px-1.5 py-0.5 text-center text-[0.65rem] font-semibold leading-none text-white">
            {unread}
          </span>
        ) : null}
      </Button>

      {open ? (
        <div
          role="dialog"
          aria-label="Notifications inbox"
          className="absolute right-0 z-40 mt-2 w-[min(22rem,calc(100vw-2rem))] rounded-2xl border border-border bg-card p-3 shadow-2xl pv-animate-popover"
        >
          <div className="flex items-center justify-between gap-3 border-b border-border pb-3">
            <div>
              <h2 className="text-sm font-semibold">Notifications</h2>
              <p className="text-xs text-muted-foreground">{unread} unread</p>
            </div>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={unread === 0 || markAllReadMutation.isPending}
              onClick={() => markAllReadMutation.mutate()}
            >
              Mark all read
            </Button>
          </div>

          {notificationsQuery.isLoading ? (
            <p className="p-4 text-sm text-muted-foreground">Loading notifications…</p>
          ) : notifications.length === 0 ? (
            <p className="p-4 text-sm text-muted-foreground">No notifications yet.</p>
          ) : (
            <ul className="max-h-96 overflow-y-auto py-2 pv-stagger">
              {notifications.map((notification) => {
                const unreadItem = !notification.readAtUtc;
                const title = notificationTitle(notification);
                const actorUserId = payloadText(notification, "byUserId");
                const actor = actorUserId ? directory.getLabel(actorUserId) : "Planvexa";

                return (
                  <li key={notification.id}>
                    <button
                      type="button"
                      className={cn(
                        "flex w-full gap-3 rounded-xl px-3 py-3 text-left text-sm hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring",
                        unreadItem ? "bg-primary/5" : "",
                      )}
                      onClick={() => markReadMutation.mutate(notification.id)}
                    >
                      <span
                        className={cn(
                          "mt-1 size-2 rounded-full",
                          unreadItem ? "bg-primary" : "bg-transparent",
                        )}
                        aria-hidden="true"
                      />
                      <span className="min-w-0 flex-1">
                        <span className="block font-medium">{title}</span>
                        <span className="block text-muted-foreground">
                          {actor} · {notification.eventType}
                        </span>
                        <time className="mt-1 block text-xs text-muted-foreground" dateTime={notification.createdAtUtc}>
                          {formatTime(notification.createdAtUtc)}
                        </time>
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}

          <div className="flex items-center justify-between border-t border-border pt-3 text-sm">
            <Link
              href="/app/notifications"
              className="rounded-md px-2 py-1 font-medium text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onClick={() => setOpen(false)}
            >
              View inbox
            </Link>
            <Link
              href="/app/notifications/preferences"
              className="rounded-md px-2 py-1 text-muted-foreground hover:text-foreground focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              onClick={() => setOpen(false)}
            >
              Preferences
            </Link>
          </div>
        </div>
      ) : null}
    </div>
  );
}
