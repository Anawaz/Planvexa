import { createHash, randomBytes } from "node:crypto";
import { NextResponse } from "next/server";
import { issuerUrl, keycloakConfig, redirectUri } from "@/lib/auth/keycloak";

const base64Url = (buffer: Buffer) => buffer.toString("base64url");

/**
 * Same PKCE/state dance as /auth/login, but starts at Keycloak's registration form instead of its
 * login form (the standard "Register" link target: the auth endpoint with /auth swapped for
 * /registrations). /auth/callback handles the return either way — it doesn't care which form the
 * user landed on.
 */
export async function GET(request: Request) {
  const url = new URL(request.url);
  const returnTo = url.searchParams.get("returnTo") ?? "/app";
  const state = base64Url(randomBytes(24));
  const verifier = base64Url(randomBytes(48));
  const challenge = base64Url(createHash("sha256").update(verifier).digest());

  const register = new URL(`${issuerUrl()}/protocol/openid-connect/registrations`);
  register.searchParams.set("client_id", keycloakConfig.clientId);
  register.searchParams.set("response_type", "code");
  register.searchParams.set("scope", "openid email profile");
  register.searchParams.set("redirect_uri", redirectUri());
  register.searchParams.set("state", state);
  register.searchParams.set("code_challenge", challenge);
  register.searchParams.set("code_challenge_method", "S256");

  const response = NextResponse.redirect(register);
  const common = { httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "lax" as const, path: "/auth/callback", maxAge: 300 };
  response.cookies.set("planvexa_pkce", verifier, common);
  response.cookies.set("planvexa_oauth_state", state, common);
  response.cookies.set("planvexa_return_to", returnTo.startsWith("/") ? returnTo : "/app", common);
  return response;
}
