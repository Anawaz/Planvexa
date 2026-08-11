import "server-only";

import { createCipheriv, createDecipheriv, createHash, randomBytes } from "node:crypto";
import { cookies } from "next/headers";

import { issuerUrl, keycloakConfig } from "@/lib/auth/keycloak";

export const sessionCookieName = "planvexa_session";
const algorithm = "aes-256-gcm";

export type SessionPayload = {
  accessToken: string;
  refreshToken?: string;
  idToken?: string;
  expiresAt: number;
  refreshExpiresAt: number;
  user: { subject: string; email?: string; name?: string };
};

function secretKey() {
  const secret = process.env.PLANVEXA_WEB_SESSION_SECRET;
  if (!secret && process.env.NODE_ENV === "production") {
    throw new Error("PLANVEXA_WEB_SESSION_SECRET is required in production.");
  }
  return createHash("sha256").update(secret ?? "planvexa-development-session-secret").digest();
}

const base64Url = (input: Buffer) => input.toString("base64url");
const fromBase64Url = (input: string) => Buffer.from(input, "base64url");

export function encryptSession(session: SessionPayload) {
  const iv = randomBytes(12);
  const cipher = createCipheriv(algorithm, secretKey(), iv);
  const ciphertext = Buffer.concat([cipher.update(JSON.stringify(session), "utf8"), cipher.final()]);
  const tag = cipher.getAuthTag();
  return `${base64Url(iv)}.${base64Url(tag)}.${base64Url(ciphertext)}`;
}

export function decryptSession(value: string): SessionPayload | null {
  try {
    const [ivValue, tagValue, ciphertextValue] = value.split(".");
    if (!ivValue || !tagValue || !ciphertextValue) return null;
    const decipher = createDecipheriv(algorithm, secretKey(), fromBase64Url(ivValue));
    decipher.setAuthTag(fromBase64Url(tagValue));
    const plaintext = Buffer.concat([decipher.update(fromBase64Url(ciphertextValue)), decipher.final()]).toString("utf8");
    const session = JSON.parse(plaintext) as SessionPayload;
    // The session lives as long as the refresh token; a stale access token is
    // what getFreshSession() is for.
    return session.refreshExpiresAt > Date.now() ? session : null;
  } catch {
    return null;
  }
}

// The encrypted payload (three Keycloak tokens) exceeds the 4096-byte cookie
// limit, so it is split across planvexa_session.0..N chunks and rejoined on read.
const maxChunkLength = 3500;
const maxChunks = 4;

const chunkName = (index: number) => `${sessionCookieName}.${index}`;

function readChunkedValue(getCookie: (name: string) => string | undefined) {
  let value = "";
  for (let index = 0; index < maxChunks; index += 1) {
    const chunk = getCookie(chunkName(index));
    if (chunk === undefined || chunk === "") break;
    value += chunk;
  }
  return value.length > 0 ? value : null;
}

export async function getSession() {
  const cookieStore = await cookies();
  const value = readChunkedValue((name) => cookieStore.get(name)?.value);
  return value ? decryptSession(value) : null;
}

function cookieAttributes(expires: Date) {
  return {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax" as const,
    path: "/",
    expires,
  };
}

export function createSessionCookies(session: SessionPayload) {
  const value = encryptSession(session);
  if (value.length > maxChunkLength * maxChunks) {
    throw new Error("Session payload exceeds maximum cookie storage.");
  }
  const descriptors = [];
  for (let index = 0; index < maxChunks; index += 1) {
    const chunk = value.slice(index * maxChunkLength, (index + 1) * maxChunkLength);
    descriptors.push({
      name: chunkName(index),
      value: chunk,
      ...cookieAttributes(chunk.length > 0 ? new Date(session.refreshExpiresAt) : new Date(0)),
    });
  }
  return descriptors;
}

// ponytail: no refresh mutex; concurrent refreshes are benign under Keycloak's default refresh-token reuse policy.
export async function getFreshSession(): Promise<{ session: SessionPayload; cookies?: ReturnType<typeof createSessionCookies> } | null> {
  const session = await getSession();
  if (!session) return null;
  if (session.expiresAt - Date.now() > 30_000) return { session };
  if (!session.refreshToken) return null;

  const response = await fetch(`${issuerUrl()}/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "refresh_token", client_id: keycloakConfig.clientId, refresh_token: session.refreshToken }),
    cache: "no-store",
  });
  if (!response.ok) return null;

  const tokens = (await response.json()) as { access_token: string; refresh_token?: string; id_token?: string; expires_in: number; refresh_expires_in?: number };
  const refreshed: SessionPayload = {
    ...session,
    accessToken: tokens.access_token,
    refreshToken: tokens.refresh_token ?? session.refreshToken,
    idToken: tokens.id_token ?? session.idToken,
    expiresAt: Date.now() + Math.max(30, tokens.expires_in - 30) * 1000,
    refreshExpiresAt: tokens.refresh_expires_in ? Date.now() + tokens.refresh_expires_in * 1000 : session.refreshExpiresAt,
  };
  return { session: refreshed, cookies: createSessionCookies(refreshed) };
}

export function expiredSessionCookies() {
  const descriptors = [{ name: sessionCookieName, value: "", ...cookieAttributes(new Date(0)) }];
  for (let index = 0; index < maxChunks; index += 1) {
    descriptors.push({ name: chunkName(index), value: "", ...cookieAttributes(new Date(0)) });
  }
  return descriptors;
}
