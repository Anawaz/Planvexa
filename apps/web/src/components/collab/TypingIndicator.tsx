"use client";

import { useTypingUsers } from "@/lib/realtime/useRealtime";
import { useCurrentUserId, useMemberDirectory } from "@/lib/members";

type TypingIndicatorProps = {
  resourceType: string;
  resourceId: string | null | undefined;
};

/** "X is typing…" — ephemeral, client-expiring (see useTypingUsers), never persisted. */
export function TypingIndicator({ resourceType, resourceId }: TypingIndicatorProps) {
  const typingUserIds = useTypingUsers(resourceType, resourceId);
  const currentUserId = useCurrentUserId();
  const directory = useMemberDirectory();
  const others = typingUserIds.filter((id) => id !== currentUserId);

  if (others.length === 0) {
    return null;
  }

  const names = others.map((id) => directory.getLabel(id));
  const text =
    names.length === 1
      ? `${names[0]} is typing…`
      : names.length === 2
        ? `${names[0]} and ${names[1]} are typing…`
        : `${names.length} people are typing…`;

  return (
    <p role="status" aria-live="polite" className="text-xs italic text-muted-foreground">
      {text}
    </p>
  );
}
