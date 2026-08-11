import { NextRequest, NextResponse } from "next/server";

const mutatingMethods = new Set(["POST", "PUT", "PATCH", "DELETE"]);

/**
 * CSRF defense for the BFF's cookie-authenticated routes.
 *
 * Reasoning: apps/api's endpoints are bearer-token authenticated (Keycloak JWT / PAT / OAuth token) and
 * are inherently CSRF-resistant — a forged cross-site request has no way to attach a custom Authorization
 * header, so classic CSRF middleware would be cargo-culted there for no benefit. The real exposure is this
 * Next.js BFF: `/api/proxy/[...path]` authenticates with the httpOnly `planvexa_session` cookie (see
 * `lib/auth/session.ts`) and then makes the bearer-authenticated call to the API on the caller's behalf —
 * that cookie IS an ambient credential a cross-site request could otherwise ride along with.
 *
 * The session cookie already sets `sameSite: "lax"`, which blocks cross-site fetch/XHR/form POST from
 * attaching it in every modern browser (Lax only forwards cookies on a top-level GET navigation, never on
 * a cross-site POST/PUT/PATCH/DELETE triggered from another origin's page). This function is
 * defense-in-depth for what SameSite alone doesn't guarantee — older/non-compliant browsers, and failing
 * closed instead of depending purely on client behaviour this app does not control.
 *
 * Verifying Origin/Sec-Fetch-Site (OWASP's "Verifying Origin With Standard Headers" CSRF mitigation) was
 * chosen over a synchronizer/double-submit token because every legitimate caller here is a same-origin
 * `fetch()` from this Next.js app itself — there is no cross-origin HTML form flow that would need a
 * token round-tripped through a page, so a token adds complexity without covering a case that exists.
 */
export function csrfRejection(request: NextRequest): NextResponse | null {
  if (!mutatingMethods.has(request.method)) return null;

  const secFetchSite = request.headers.get("sec-fetch-site");
  if (secFetchSite) {
    return secFetchSite === "same-origin" || secFetchSite === "none" ? null : forbidden();
  }

  // Fallback for the rare client that omits Sec-Fetch-Site (Fetch Metadata is broadly supported by
  // evergreen browsers, but this keeps the check meaningful rather than a silent no-op for the rest).
  const origin = request.headers.get("origin");
  if (!origin) return forbidden();

  // Compare against the Host the client actually addressed, not request.nextUrl.origin: behind any
  // reverse proxy/dev-server port-forward (this app's own local dev stack included -- Next listens on
  // an internal ephemeral port while the AppHost fronts it on :3000), nextUrl reflects Next's internal
  // bind address, which never matches a legitimate same-origin request's Origin header.
  const forwardedHost = request.headers.get("x-forwarded-host");
  const host = forwardedHost ?? request.headers.get("host");
  const protocol = request.headers.get("x-forwarded-proto") ?? request.nextUrl.protocol.replace(":", "");
  const expectedOrigin = host ? `${protocol}://${host}` : request.nextUrl.origin;

  return origin === expectedOrigin ? null : forbidden();
}

function forbidden() {
  return NextResponse.json(
    { type: "about:blank", title: "Forbidden", status: 403, detail: "Cross-site request blocked." },
    { status: 403 },
  );
}
