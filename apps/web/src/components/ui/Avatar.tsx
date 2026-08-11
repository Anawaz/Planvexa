"use client";

import { proxyHref } from "@/lib/api-client";

/**
 * Renders a user's uploaded avatar image when present, else the initials fallback the call site
 * already computed (see useMemberDirectory().getInitials). `avatarUrl` is the relative API path the
 * backend returns (e.g. `/users/{id}/avatar`) — run through proxyHref so the request rides the
 * browser's session cookie instead of needing a bearer header on an <img> tag, same convention as the
 * document/whiteboard inline image components (see documentImageHref).
 */
export function Avatar({
  avatarUrl,
  initials,
  className,
  title,
}: {
  avatarUrl?: string | null;
  initials: string;
  className: string;
  title?: string;
}) {
  if (avatarUrl) {
    return (
      // eslint-disable-next-line @next/next/no-img-element -- authenticated proxy URL, next/image can't fetch it.
      <img src={proxyHref(avatarUrl)} alt="" title={title} className={`${className} object-cover`} />
    );
  }

  return (
    <span title={title} className={className}>
      {initials}
    </span>
  );
}
