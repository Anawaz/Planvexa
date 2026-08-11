import { NextResponse } from "next/server";
import { getFreshSession } from "@/lib/auth/session";

export async function GET() {
  const fresh = await getFreshSession();
  if (!fresh) {
    // 200, not 401: this is a session probe, and it runs on public pages too. A 401 would show up
    // as a console error on every unauthenticated page load for no benefit — the consumer only
    // looks at the body.
    return NextResponse.json({ user: null });
  }

  const response = NextResponse.json({ user: fresh.session.user, expiresAt: fresh.session.expiresAt });
  for (const cookie of fresh.cookies ?? []) {
    response.cookies.set(cookie);
  }
  return response;
}
