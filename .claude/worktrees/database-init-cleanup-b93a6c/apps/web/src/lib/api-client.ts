const DEFAULT_PROXY_BASE_URL = "/api/proxy";

export const API_BASE_URL = process.env.NEXT_PUBLIC_PLANVEXA_API_PROXY?.replace(/\/$/, "") ?? DEFAULT_PROXY_BASE_URL;

export type ApiRequestOptions = Omit<RequestInit, "body" | "method"> & {
  workspaceId?: string;
  idempotencyKey?: string;
};

// Ambient workspace for every request; AppContextProvider keeps it in sync.
// Module state (not a cookie) so two tabs can sit in different workspaces.
let apiContext: { workspaceId?: string } = {};

export function setApiContext(context: { workspaceId?: string }) {
  apiContext = context;
}

/** Read-only peek at the ambient workspace — used by the offline cache to key writes
 * without every call site having to thread `workspaceId` through explicitly. */
export function getApiContext() {
  return apiContext;
}

export class ApiError extends Error {
  constructor(message: string, readonly status: number, readonly details: unknown, readonly correlationId?: string | null) {
    super(message);
    this.name = "ApiError";
  }
}

function buildUrl(path: string) {
  if (/^https?:\/\//i.test(path)) return path;
  return `${API_BASE_URL}/${path.replace(/^\/api\/v1\//, "").replace(/^\//, "")}`;
}

async function parsePayload(response: Response) {
  if (response.status === 204) return undefined;
  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("application/json")) return response.json();
  const text = await response.text();
  return text.length > 0 ? text : undefined;
}

async function request<TResponse, TBody = unknown>(
  method: "GET" | "POST" | "PUT" | "PATCH" | "DELETE",
  path: string,
  options: ApiRequestOptions & { body?: TBody } = {},
): Promise<TResponse> {
  const { body, headers: initHeaders, workspaceId, idempotencyKey, ...init } = options;
  const headers = new Headers(initHeaders);
  headers.set("Accept", "application/json");
  const effectiveWorkspace = workspaceId ?? apiContext.workspaceId;
  if (effectiveWorkspace) headers.set("X-Workspace", effectiveWorkspace);
  if (idempotencyKey) headers.set("Idempotency-Key", idempotencyKey);

  const requestInit: RequestInit = { ...init, method, headers };
  if (body !== undefined) {
    if (body instanceof FormData) {
      requestInit.body = body;
    } else {
      headers.set("Content-Type", "application/json");
      requestInit.body = JSON.stringify(body);
    }
  }

  const response = await fetch(buildUrl(path), requestInit);
  const payload = await parsePayload(response);
  if (!response.ok) {
    const message = typeof payload === "object" && payload && "title" in payload
      ? String((payload as { title?: unknown }).title)
      : `Request failed with ${response.status}`;
    throw new ApiError(message, response.status, payload, response.headers.get("X-Correlation-Id"));
  }

  return payload as TResponse;
}

/**
 * Same-origin proxy URL for browser navigations (CSV/file downloads). A plain `<a href>` cannot set
 * headers, so the ambient workspace rides along as a query param and the proxy turns it back into
 * `X-Workspace`.
 */
export function proxyHref(path: string, params: Record<string, string | undefined> = {}) {
  const url = new URL(buildUrl(path), "http://proxy.local");
  for (const [key, value] of Object.entries(params)) {
    if (value) url.searchParams.set(key, value);
  }
  if (apiContext.workspaceId) url.searchParams.set("x-workspace", apiContext.workspaceId);
  return `${url.pathname}${url.search}`;
}

export const apiClient = {
  get<TResponse>(path: string, options?: ApiRequestOptions) { return request<TResponse>("GET", path, options); },
  post<TResponse, TBody = unknown>(path: string, body?: TBody, options?: ApiRequestOptions) { return request<TResponse, TBody>("POST", path, { ...options, body }); },
  put<TResponse, TBody = unknown>(path: string, body?: TBody, options?: ApiRequestOptions) { return request<TResponse, TBody>("PUT", path, { ...options, body }); },
  patch<TResponse, TBody = unknown>(path: string, body?: TBody, options?: ApiRequestOptions) { return request<TResponse, TBody>("PATCH", path, { ...options, body }); },
  delete<TResponse>(path: string, options?: ApiRequestOptions) { return request<TResponse>("DELETE", path, options); },
};
