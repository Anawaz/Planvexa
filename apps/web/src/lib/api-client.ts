const DEFAULT_PROXY_BASE_URL = "/api/proxy";

export const API_BASE_URL = process.env.NEXT_PUBLIC_PLANVEXA_API_PROXY?.replace(/\/$/, "") ?? DEFAULT_PROXY_BASE_URL;

export type ApiRequestOptions = Omit<RequestInit, "body" | "method"> & {
  workspaceId?: string;
  idempotencyKey?: string;
  /**
   * Send the request with NO `X-Workspace` header, ignoring the ambient workspace. Required by the
   * host administration console: `/api/v1/host/*` is instance-level and its cross-workspace reads
   * depend on there being no ambient workspace at all (an `X-Workspace` header would make the API
   * resolve one and its RLS policies would then filter every row to that single workspace). The
   * ambient `apiContext` is module state that survives client-side navigation out of `/app`, so
   * "just don't be in a workspace" is not something the caller can rely on.
   */
  noWorkspace?: boolean;
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

/** The RFC 9457 problem "type" WorkspaceResolutionMiddleware returns when a Workspace's MfaRequired
 * setting blocks the caller — distinct from a plain 403 so the UI can show a specific remediation
 * screen instead of a generic access-denied message. */
const MFA_REQUIRED_PROBLEM_TYPE = "https://planvexa.dev/problems/mfa-required";

export function isMfaRequiredError(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    error.status === 403 &&
    typeof error.details === "object" &&
    error.details !== null &&
    "type" in error.details &&
    (error.details as { type?: unknown }).type === MFA_REQUIRED_PROBLEM_TYPE
  );
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
  const { body, headers: initHeaders, workspaceId, idempotencyKey, noWorkspace, ...init } = options;
  const headers = new Headers(initHeaders);
  headers.set("Accept", "application/json");
  const effectiveWorkspace = noWorkspace ? undefined : workspaceId ?? apiContext.workspaceId;
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
    // ProblemDetails' "title" is a generic per-status label (e.g. "Bad Request"); the caller-safe
    // specific reason (e.g. "ClickUp import is not yet implemented…") is in "detail". Prefer it so
    // every mutation error banner in the app shows the actual reason, not just the status name.
    const problem = typeof payload === "object" && payload ? (payload as { title?: unknown; detail?: unknown }) : undefined;
    const message = problem && typeof problem.detail === "string" && problem.detail.length > 0
      ? problem.detail
      : problem && "title" in problem
        ? String(problem.title)
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
  delete<TResponse, TBody = unknown>(path: string, body?: TBody, options?: ApiRequestOptions) { return request<TResponse, TBody>("DELETE", path, { ...options, body }); },
};
