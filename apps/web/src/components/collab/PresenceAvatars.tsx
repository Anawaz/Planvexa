"use client";

import { useQuery } from "@tanstack/react-query";
import { getPresence } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import { useAppContext } from "@/lib/app-context/AppContext";
import { Avatar } from "@/components/ui/Avatar";
import { useMemberDirectory } from "@/lib/members";
import { useRealtimePresence } from "@/lib/realtime/useRealtime";

export function PresenceAvatars() {
  const { workspaceId } = useAppContext();
  const directory = useMemberDirectory();
  // Seed only: the hub's `presence` event takes over as soon as anyone joins or leaves.
  const presenceQuery = useQuery({
    queryKey: collabKeys.presence(workspaceId ?? ""),
    queryFn: () => getPresence(workspaceId!),
    enabled: Boolean(workspaceId),
  });
  const livePresence = useRealtimePresence();

  const userIds = livePresence.length > 0 ? livePresence : (presenceQuery.data ?? []);

  if (userIds.length === 0) {
    return null;
  }

  const visibleUsers = userIds.slice(0, 4);
  const overflow = Math.max(0, userIds.length - visibleUsers.length);

  return (
    <div className="hidden items-center gap-2 md:flex" aria-label={`${userIds.length} teammates present`}>
      <span className="text-xs text-muted-foreground">Live</span>
      <div className="flex -space-x-2">
        {visibleUsers.map((userId) => (
          <Avatar
            key={userId}
            title={directory.getLabel(userId)}
            avatarUrl={directory.getAvatarUrl(userId)}
            initials={directory.getInitials(userId)}
            className="grid size-8 place-items-center rounded-full border-2 border-background bg-muted text-xs font-semibold shadow-sm"
          />
        ))}
        {overflow > 0 ? (
          <span className="grid size-8 place-items-center rounded-full border-2 border-background bg-card text-xs font-semibold shadow-sm">
            +{overflow}
          </span>
        ) : null}
      </div>
    </div>
  );
}
