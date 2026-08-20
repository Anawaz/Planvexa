import { NextRequest, NextResponse } from "next/server";

export function middleware(request: NextRequest) {
  const session = request.cookies.get("planvexa_session.0")?.value;
  if (!session) {
    const loginUrl = new URL("/login", request.url);
    loginUrl.searchParams.set("returnTo", `${request.nextUrl.pathname}${request.nextUrl.search}`);
    return NextResponse.redirect(loginUrl);
  }
  return NextResponse.next();
}

// `/host` is the instance-level administration console. It is listed here only so an unauthenticated
// visitor is redirected to sign in rather than rendering its shell; whether a signed-in user is
// actually a host administrator is decided server-side by the HostAdmin policy on every
// /api/v1/host/* call (and mirrored by the layout's own gate, which only decides what to render).
export const config = { matcher: ["/app/:path*", "/host/:path*", "/invite/:path*", "/onboarding/:path*"] };
