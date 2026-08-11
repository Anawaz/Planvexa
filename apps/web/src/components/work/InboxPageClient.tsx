"use client";

import Link from "next/link";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/Button";
import {
  notificationHref,
  notificationTitle,
  payloadText,
} from "@/components/notifications/NotificationBell";
import { useNotificationMutations } from "@/components/notifications/useNotificationMutations";
import { listNotifications, unreadCount } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import { useMemberDirectory } from "@/lib/members";
import { listMyTasks } from "@/lib/work/client";
import { workKeys } from "@/lib/work/queries";
import { cn } from "@/lib/utils";

function formatTime(value: string) {
  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

/**
 * Inbox -- a per-user aggregated feed, distinct from My Work (task-focused list/board) by being
 * activity/notification-focused. Reuses the existing Notifications backend (NotificationInboxService,
 * GET /api/v1/notifications) rather than a new model -- see the design brief: mentions, watched-task
 * updates and assignment events already flow through that generic event system. The only addition here
 * is a compact "due soon" strip drawn from the existing My Work task list, so the page reads as a single
 * "what needs my attention" surface instead of two separate pages.
 */
export function InboxPageClient() {
  const [unreadOnly, setUnreadOnly] = useState(false);
  const notificationsQuery = useQuery({
    queryKey: collabKeys.notifications({ unreadOnly }),
    queryFn: () => listNotifications({ unreadOnly }),
  });
  const countQuery = useQuery({ queryKey: collabKeys.unreadCount(), queryFn: unreadCount });
  const myTasksQuery = useQuery({ queryKey: workKeys.myTasks(), queryFn: () => listMyTasks() });
  const directory = useMemberDirectory();
  const { markReadMutation, markAllReadMutation } = useNotificationMutations();

  const notifications = notificationsQuery.data ?? [];
  const unread = countQuery.data ?? 0;
  const dueSoon = (myTasksQuery.data ?? [])
    .filter((task) => !task.isCompleted && task.dueDate)
    .slice(0, 5);

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <p className="text-sm font-medium uppercase tracking-wide text-muted-foreground">Workspace</p>
          <h1 className="text-3xl font-semibold tracking-tight">Inbox</h1>
          <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
            Everything that needs your attention: mentions, watched-task updates and assignment activity,
            plus what&apos;s due soon. For your full task list, see{" "}
            <Link href="/app/my-work" className="text-primary underline-offset-2 hover:underline">
              My Work
            </Link>
            .
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          disabled={unread === 0 || markAllReadMutation.isPending}
          onClick={() => markAllReadMutation.mutate()}
        >
          Mark all read
        </Button>
      </div>

      {dueSoon.length > 0 ? (
        <section aria-labelledby="inbox-due-soon" className="rounded-2xl border border-border bg-card p-4">
          <h2 id="inbox-due-soon" className="text-sm font-semibold">
            Due soon
          </h2>
          <ul className="mt-3 flex flex-wrap gap-2">
            {dueSoon.map((task) => (
              <li key={task.id}>
                <Link
                  href={`/app/lists/${task.listId}`}
                  className="inline-flex items-center gap-2 rounded-full border border-border bg-background px-3 py-1.5 text-xs font-medium hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
                >
                  {task.title}
                  {task.dueDate ? (
                    <span className="text-muted-foreground">
                      {new Intl.DateTimeFormat("en", { month: "short", day: "numeric" }).format(
                        new Date(task.dueDate),
                      )}
                    </span>
                  ) : null}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-card p-3">
        <div className="inline-flex rounded-xl bg-muted p-1" role="group" aria-label="Inbox filters">
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
          Nothing needs your attention right now.
        </div>
      ) : (
        <ol className="space-y-3">
          {notifications.map((notification) => {
            const unreadItem = !notification.readAtUtc;
            const title = notificationTitle(notification);
            const actorUserId = payloadText(notification, "byUserId");
            const actor = actorUserId ? directory.getLabel(actorUserId) : "Planvexa";
            const href = notificationHref(notification);

            return (
              <li key={notification.id} className="rounded-2xl border border-border bg-card p-4 shadow-sm">
                <div className="flex gap-3">
                  <span
                    className={cn("mt-2 size-2 rounded-full", unreadItem ? "bg-primary" : "bg-transparent")}
                    aria-hidden="true"
                  />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      {href ? (
                        <h2 className="font-semibold">
                          <Link
                            href={href}
                            className="underline-offset-2 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-ring"
                            onClick={() => markReadMutation.mutate(notification.id)}
                          >
                            {title}
                          </Link>
                        </h2>
                      ) : (
                        <h2 className="font-semibold">{title}</h2>
                      )}
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
