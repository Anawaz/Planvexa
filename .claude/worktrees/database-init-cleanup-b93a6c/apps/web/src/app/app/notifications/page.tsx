"use client";

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import { listNotifications, unreadCount } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import { useMemberDirectory } from "@/lib/members";
import { cn } from "@/lib/utils";
import {
  notificationTitle,
  payloadText,
} from "@/components/notifications/NotificationBell";
import { useNotificationMutations } from "@/components/notifications/useNotificationMutations";

function formatTime(value: string) {
  return new Intl.DateTimeFormat("en", {
    weekday: "short",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

export default function NotificationsPage() {
  const [unreadOnly, setUnreadOnly] = useState(false);
  const notificationsQuery = useQuery({
    queryKey: collabKeys.notifications({ unreadOnly }),
    queryFn: () => listNotifications({ unreadOnly }),
  });
  const countQuery = useQuery({ queryKey: collabKeys.unreadCount(), queryFn: unreadCount });
  const directory = useMemberDirectory();
  const { markReadMutation, markAllReadMutation } = useNotificationMutations();
  const notifications = notificationsQuery.data ?? [];
  const unread = countQuery.data ?? 0;

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-wide text-muted-foreground">
            Collaboration
          </p>
          <h1 className="text-3xl font-semibold tracking-tight">Notifications</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            Mentions and automation notifications routed to your workspace inbox.
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            disabled={unread === 0 || markAllReadMutation.isPending}
            onClick={() => markAllReadMutation.mutate()}
          >
            Mark all read
          </Button>
          <Link
            href="/app/notifications/preferences"
            className="inline-flex h-11 items-center rounded-lg border border-border bg-card px-4 text-sm font-medium hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            Preferences
          </Link>
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-card p-3">
        <div className="inline-flex rounded-xl bg-muted p-1" role="group" aria-label="Notification filters">
          <button
            type="button"
            className={cn(
              "rounded-lg px-3 py-2 text-sm font-medium focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring",
              !unreadOnly ? "bg-card shadow-sm" : "text-muted-foreground",
            )}
            aria-pressed={!unreadOnly}
            onClick={() => setUnreadOnly(false)}
          >
            All
          </button>
          <button
            type="button"
            className={cn(
              "rounded-lg px-3 py-2 text-sm font-medium focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring",
              unreadOnly ? "bg-card shadow-sm" : "text-muted-foreground",
            )}
            aria-pressed={unreadOnly}
            onClick={() => setUnreadOnly(true)}
          >
            Unread ({unread})
          </button>
        </div>
        <p className="text-sm text-muted-foreground">{notifications.length} shown</p>
      </div>

      {notificationsQuery.isLoading ? (
        <div className="rounded-2xl border border-border bg-card p-6 text-sm text-muted-foreground">
          Loading inbox…
        </div>
      ) : notifications.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-border bg-card p-8 text-center text-sm text-muted-foreground">
          No notifications match this filter.
        </div>
      ) : (
        <ol className="space-y-3">
          {notifications.map((notification) => {
            const unreadItem = !notification.readAtUtc;
            const title = notificationTitle(notification);
            const actorUserId = payloadText(notification, "byUserId");
            const actor = actorUserId ? directory.getLabel(actorUserId) : "Planvexa";

            return (
              <li key={notification.id} className="rounded-2xl border border-border bg-card p-4 shadow-sm">
                <div className="flex gap-3">
                  <span
                    className={cn("mt-2 size-2 rounded-full", unreadItem ? "bg-primary" : "bg-transparent")}
                    aria-hidden="true"
                  />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <h2 className="font-semibold">{title}</h2>
                      <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">
                        {notification.eventType}
                      </span>
                    </div>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {actor} · {notification.entityType} {notification.entityId.slice(0, 8)}
                    </p>
                    <time className="mt-2 block text-xs text-muted-foreground" dateTime={notification.createdAtUtc}>
                      {formatTime(notification.createdAtUtc)}
                    </time>
                  </div>
                  {unreadItem ? (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      disabled={markReadMutation.isPending}
                      onClick={() => markReadMutation.mutate(notification.id)}
                    >
                      Mark read
                    </Button>
                  ) : null}
                </div>
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}
