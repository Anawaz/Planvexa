"use client";

import Link from "next/link";
import { useInstanceBranding } from "@/lib/branding/client";
import { cn } from "@/lib/utils";

/**
 * The instance's name (and logo, when the operator has set one), as shown in every shell header.
 *
 * Exists so a rename under /host/settings actually reaches the UI: before this, `instanceName` was
 * written by the host console and read by nothing, and every header hardcoded "Planvexa".
 */
export function Wordmark({
  href = "/app",
  className,
  suffix,
}: {
  href?: string;
  className?: string;
  /** Rendered after the name — the host console's "Host" pill. */
  suffix?: React.ReactNode;
}) {
  const { instanceName, logoUrl } = useInstanceBranding();

  return (
    <Link href={href} className={cn("flex min-w-0 items-center gap-2", className)}>
      {logoUrl ? (
        // Plain <img>, not next/image: the URL is operator-supplied and can point at any host, which
        // next/image refuses without that host in remotePatterns — unknowable at build time. Height is
        // capped so an oversized logo cannot blow out the 4rem header.
        // eslint-disable-next-line @next/next/no-img-element
        <img src={logoUrl} alt="" aria-hidden="true" className="h-7 w-auto max-w-32 shrink-0 object-contain" />
      ) : null}
      <span className="truncate text-lg font-semibold tracking-tight">{instanceName}</span>
      {suffix}
    </Link>
  );
}
