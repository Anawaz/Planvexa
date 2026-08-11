import { NextResponse } from "next/server";
import { appUrl, issuerUrl, keycloakConfig } from "@/lib/auth/keycloak";
import { expiredSessionCookies, getSession } from "@/lib/auth/session";

export async function GET() {
  const session = await getSession();
  const logout = new URL(`${issuerUrl()}/protocol/openid-connect/logout`);
  logout.searchParams.set("client_id", keycloakConfig.clientId);
  logout.searchParams.set("post_logout_redirect_uri", appUrl("/login"));
  // Without the hint Keycloak cannot prove which session is ending and interrupts the user with
  // its "Do you want to log out?" confirmation page.
  if (session?.idToken) {
    logout.searchParams.set("id_token_hint", session.idToken);
  }
  const response = NextResponse.redirect(logout);
  for (const cookie of expiredSessionCookies()) {
    response.cookies.set(cookie);
  }
  return response;
}
