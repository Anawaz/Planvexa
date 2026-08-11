import { NextResponse } from "next/server";
import { getFreshSession } from "@/lib/auth/session";

export async function GET() {
  const fresh = await getFreshSession();
  if (!fresh) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const response = NextResponse.json({ accessToken: fresh.session.accessToken });
  for (const cookie of fresh.cookies ?? []) {
    response.cookies.set(cookie);
  }
  return response;
}
