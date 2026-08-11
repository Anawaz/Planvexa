import { NextRequest, NextResponse } from "next/server";
import { getFreshSession } from "@/lib/auth/session";
import { csrfRejection } from "@/lib/security/csrf";

const API_BASE_URL = (process.env.API_BASE_URL ?? process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080").replace(/\/$/, "");
const hopByHopHeaders = new Set(["connection", "keep-alive", "proxy-authenticate", "proxy-authorization", "te", "trailer", "transfer-encoding", "upgrade", "host", "cookie"]);

type RouteContext = { params: Promise<{ path?: string[] }> };

// Query-param aliases for callers that cannot set headers (plain <a> downloads). Stripped before
// forwarding and re-applied as headers.
const contextParams: Record<string, string> = { "x-workspace": "X-Workspace" };

async function proxy(request: NextRequest, context: RouteContext) {
  const csrfBlock = csrfRejection(request);
  if (csrfBlock) return csrfBlock;

  const { path = [] } = await context.params;
  // `/api/v1/public/*` is AllowAnonymous on the API; pass it through so browser-side public form
  // submits stay same-origin (the API registers no CORS policy).
  const isAnonymous = path[0] === "public";

  const fresh = isAnonymous ? null : await getFreshSession();
  if (!isAnonymous && !fresh) {
    return NextResponse.json({ type: "about:blank", title: "Unauthorized", status: 401 }, { status: 401 });
  }

  const target = new URL(`${API_BASE_URL}/api/v1/${path.map(encodeURIComponent).join("/")}`);
  request.nextUrl.searchParams.forEach((value, key) => {
    if (!(key in contextParams)) target.searchParams.append(key, value);
  });

  const headers = new Headers();
  request.headers.forEach((value, key) => {
    if (!hopByHopHeaders.has(key.toLowerCase())) {
      headers.set(key, value);
    }
  });
  for (const [param, header] of Object.entries(contextParams)) {
    const value = request.nextUrl.searchParams.get(param);
    if (value && !headers.has(header)) headers.set(header, value);
  }
  if (fresh) headers.set("Authorization", `Bearer ${fresh.session.accessToken}`);
  headers.set("Accept", request.headers.get("accept") ?? "application/json");

  const requestInit: RequestInit = { method: request.method, headers, cache: "no-store", redirect: "manual" };
  if (!['GET', 'HEAD'].includes(request.method)) {
    requestInit.body = await request.arrayBuffer();
  }

  const response = await fetch(target, requestInit);
  const responseHeaders = new Headers();
  response.headers.forEach((value, key) => {
    if (!hopByHopHeaders.has(key.toLowerCase())) {
      responseHeaders.set(key, value);
    }
  });

  const proxied = new NextResponse(response.body, { status: response.status, statusText: response.statusText, headers: responseHeaders });
  for (const cookie of fresh?.cookies ?? []) {
    proxied.cookies.set(cookie);
  }
  return proxied;
}

export const GET = proxy;
export const POST = proxy;
export const PUT = proxy;
export const PATCH = proxy;
export const DELETE = proxy;
