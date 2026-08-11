"use client";

import { useMutation, useQueryClient, type QueryKey } from "@tanstack/react-query";
import { markAllRead as markAllReadRequest, markRead as markReadRequest } from "@/lib/collab/client";
import { collabKeys } from "@/lib/collab/queries";
import type { Notification } from "@/lib/collab/types";

type NotificationSnapshot = {
  lists: Array<[QueryKey, Notification[] | undefined]>;
  unreadCount?: number;
};

function restoreSnapshot(
  queryClient: ReturnType<typeof useQueryClient>,
  snapshot?: NotificationSnapshot,
) {
  snapshot?.lists.forEach(([queryKey, data]) => {
    queryClient.setQueryData(queryKey, data);
  });
  queryClient.setQueryData(collabKeys.unreadCount(), snapshot?.unreadCount);
}

export function useNotificationMutations() {
  const queryClient = useQueryClient();

  const markReadMutation = useMutation({
    mutationFn: markReadRequest,
    onMutate: async (id: string): Promise<NotificationSnapshot> => {
      await queryClient.cancelQueries({ queryKey: collabKeys.notificationsRoot() });
      await queryClient.cancelQueries({ queryKey: collabKeys.unreadCount() });
      const lists = queryClient.getQueriesData<Notification[]>({
        queryKey: collabKeys.notificationsRoot(),
      });
      const unreadCount = queryClient.getQueryData<number>(collabKeys.unreadCount());
      const readAtUtc = new Date().toISOString();
      const wasUnread = lists.some(([, data]) =>
        data?.some((notification) => notification.id === id && !notification.readAtUtc),
      );

      queryClient.setQueriesData<Notification[]>(
        { queryKey: collabKeys.notificationsRoot() },
        (current) =>
          current?.map((notification) =>
            notification.id === id ? { ...notification, readAtUtc } : notification,
          ),
      );
      queryClient.setQueryData<number>(collabKeys.unreadCount(), (current = 0) =>
        wasUnread ? Math.max(0, current - 1) : current,
      );

      return { lists, unreadCount };
    },
    onError: (_error, _variables, snapshot) => {
      restoreSnapshot(queryClient, snapshot);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: collabKeys.notificationsRoot() });
      void queryClient.invalidateQueries({ queryKey: collabKeys.unreadCount() });
    },
  });

  const markAllReadMutation = useMutation({
    mutationFn: markAllReadRequest,
    onMutate: async (): Promise<NotificationSnapshot> => {
      await queryClient.cancelQueries({ queryKey: collabKeys.notificationsRoot() });
      await queryClient.cancelQueries({ queryKey: collabKeys.unreadCount() });
      const lists = queryClient.getQueriesData<Notification[]>({
        queryKey: collabKeys.notificationsRoot(),
      });
      const unreadCount = queryClient.getQueryData<number>(collabKeys.unreadCount());
      const readAtUtc = new Date().toISOString();

      queryClient.setQueriesData<Notification[]>(
        { queryKey: collabKeys.notificationsRoot() },
        (current) => current?.map((notification) => ({ ...notification, readAtUtc })),
      );
      queryClient.setQueryData(collabKeys.unreadCount(), 0);

      return { lists, unreadCount };
    },
    onError: (_error, _variables, snapshot) => {
      restoreSnapshot(queryClient, snapshot);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: collabKeys.notificationsRoot() });
      void queryClient.invalidateQueries({ queryKey: collabKeys.unreadCount() });
    },
  });

  return { markReadMutation, markAllReadMutation };
}
