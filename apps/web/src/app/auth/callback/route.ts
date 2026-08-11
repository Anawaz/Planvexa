import { NextRequest, NextResponse } from "next/server";
import { appUrl, issuerUrl, keycloakConfig, redirectUri } from "@/lib/auth/keycloak";
import { createSessionCookies } from "@/lib/auth/session";

type TokenResponse = { access_token: string; refresh_token?: string; id_token?: string; expires_in: number; refresh_expires_in?: number };
type UserInfo = { sub: string; email?: string; name?: string; preferred_username?: string };

export async function GET(request: NextRequest) {
  const url = new URL(request.url);
  const code = url.searchParams.get("code");
  const state = url.searchParams.get("state");
  const expectedState = request.cookies.get("planvexa_oauth_state")?.value;
  const verifier = request.cookies.get("planvexa_pkce")?.value;
  const returnTo = request.cookies.get("planvexa_return_to")?.value ?? "/app";
  if (!code || !state || !expectedState || state !== expectedState || !verifier) {
    return NextResponse.redirect(appUrl("/session-expired"));
  }

  const form = new URLSearchParams({ grant_type: "authorization_code", client_id: keycloakConfig.clientId, code, redirect_uri: redirectUri(), code_verifier: verifier });
  const tokenResponse = await fetch(`${issuerUrl()}/protocol/openid-connect/token`, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: form, cache: "no-store" });
  if (!tokenResponse.ok) return NextResponse.redirect(appUrl("/access-denied"));
  const tokens = (await tokenResponse.json()) as TokenResponse;

  const userInfoResponse = await fetch(`${issuerUrl()}/protocol/openid-connect/userinfo`, { headers: { Authorization: `Bearer ${tokens.access_token}` }, cache: "no-store" });
  const user = userInfoResponse.ok ? ((await userInfoResponse.json()) as UserInfo) : { sub: "unknown" };

  const expiresAt = Date.now() + Math.max(30, tokens.expires_in - 30) * 1000;
  const response = NextResponse.redirect(appUrl(returnTo.startsWith("/") ? returnTo : "/app"));
  for (const cookie of createSessionCookies({
    accessToken: tokens.access_token,
    refreshToken: tokens.refresh_token,
    idToken: tokens.id_token,
    expiresAt,
    refreshExpiresAt: tokens.refresh_expires_in ? Date.now() + tokens.refresh_expires_in * 1000 : expiresAt + 30 * 60 * 1000,
    user: { subject: user.sub, email: user.email, name: user.name ?? user.preferred_username },
  })) {
    response.cookies.set(cookie);
  }
  response.cookies.delete("planvexa_pkce");
  response.cookies.delete("planvexa_oauth_state");
  response.cookies.delete("planvexa_return_to");
  return response;
}
