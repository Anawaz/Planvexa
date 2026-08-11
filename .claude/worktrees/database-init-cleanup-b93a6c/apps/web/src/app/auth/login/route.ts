import { createHash, randomBytes } from "node:crypto";
import { NextResponse } from "next/server";
import { issuerUrl, keycloakConfig, redirectUri } from "@/lib/auth/keycloak";

const base64Url = (buffer: Buffer) => buffer.toString("base64url");

export async function GET(request: Request) {
  const url = new URL(request.url);
  const returnTo = url.searchParams.get("returnTo") ?? "/app";
  const state = base64Url(randomBytes(24));
  const verifier = base64Url(randomBytes(48));
  const challenge = base64Url(createHash("sha256").update(verifier).digest());

  const authorize = new URL(`${issuerUrl()}/protocol/openid-connect/auth`);
  authorize.searchParams.set("client_id", keycloakConfig.clientId);
  authorize.searchParams.set("response_type", "code");
  authorize.searchParams.set("scope", "openid email profile");
  authorize.searchParams.set("redirect_uri", redirectUri());
  authorize.searchParams.set("state", state);
  authorize.searchParams.set("code_challenge", challenge);
  authorize.searchParams.set("code_challenge_method", "S256");

  const response = NextResponse.redirect(authorize);
  const common = { httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "lax" as const, path: "/auth/callback", maxAge: 300 };
  response.cookies.set("planvexa_pkce", verifier, common);
  response.cookies.set("planvexa_oauth_state", state, common);
  response.cookies.set("planvexa_return_to", returnTo.startsWith("/") ? returnTo : "/app", common);
  return response;
}
