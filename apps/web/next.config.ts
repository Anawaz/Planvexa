import type { NextConfig } from "next";

// CSP + security headers. Built once at startup (not per-request) since the inputs
// (NEXT_PUBLIC_* origins) are already build-time constants in this app — see the E2E CI job's comment on
// next.config.ts: "NEXT_PUBLIC_* are inlined at build time, so they must be set before `npm run build`".
function connectOrigins() {
  const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";
  const collabWs = process.env.NEXT_PUBLIC_COLLAB_WS_URL ?? "ws://localhost:1234";

  // The browser talks to the API directly for SignalR's WebSocket upgrade (apps/web/src/lib/realtime/
  // useRealtime.ts connects to `${API_BASE}/hubs/workspace`) and to the Hocuspocus collaboration server
  // directly for Yjs sync (apps/web/src/lib/collab/hocuspocusProvider.ts) — both bypass the Next.js BFF
  // proxy (WebSockets aren't proxied by it), so both origins need to be allowed here, in both their
  // http(s) and ws(s) forms since SignalR's negotiate step is a plain HTTP(S) call before the upgrade.
  const apiWs = apiBase.replace(/^http/, "ws");
  return [apiBase, apiWs, collabWs].join(" ");
}

function buildCsp() {
  const connect = connectOrigins();
  const directives = [
    "default-src 'self'",
    // ponytail: 'unsafe-inline' on script/style, not a nonce-based policy — Next.js App Router injects
    // inline bootstrap scripts for RSC streaming and this app has no per-request nonce plumbing yet.
    // Upgrade path: middleware-generated nonce forwarded via header + `<script nonce>` in the root layout
    // (Next.js's documented pattern: https://nextjs.org/docs/app/guides/content-security-policy) if a
    // stricter script-src is ever required.
    // 'unsafe-eval' only in dev: Next.js/Turbopack's HMR and React DevTools use eval() for stack-frame
    // reconstruction in development; React never calls eval() in a production build (verified via the
    // browser console warning this fixes), so production keeps the stricter policy.
    process.env.NODE_ENV === "production"
      ? "script-src 'self' 'unsafe-inline'"
      : "script-src 'self' 'unsafe-inline' 'unsafe-eval'",
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob: https:",
    "font-src 'self' data:",
    `connect-src 'self' ${connect}`,
    "frame-ancestors 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "object-src 'none'",
  ];
  return directives.join("; ");
}

const securityHeaders = [
  { key: "Content-Security-Policy", value: buildCsp() },
  { key: "X-Content-Type-Options", value: "nosniff" },
  // Same guarantee as CSP's frame-ancestors 'none' above, kept for browsers that only honour the legacy
  // header.
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  ...(process.env.NODE_ENV === "production"
    ? [{ key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains; preload" }]
    : []),
];

const nextConfig: NextConfig = {
  // Self-contained server bundle for the container image. No effect on `next dev`.
  output: "standalone",

  async headers() {
    return [
      {
        source: "/:path*",
        headers: securityHeaders,
      },
    ];
  },
};

export default nextConfig;
