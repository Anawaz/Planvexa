"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useQueryClient } from "@tanstack/react-query";
import { buttonStyles } from "@/components/ui/Button";
import { ApiError, apiClient } from "@/lib/api-client";
import { useAppContext } from "@/lib/app-context/AppContext";

type AcceptResponse = { membershipId: string; workspaceId: string; role: string };

type Status =
  | { kind: "accepting" }
  | { kind: "accepted"; response: AcceptResponse }
  | { kind: "error"; message: string };

/**
 * Invitation acceptance landing page. The invitation email links here with the single-use token; the
 * page exchanges it for a workspace membership via the API. Auth is enforced by middleware, so the
 * user is already signed in (register-then-accept flows return here via the login `returnTo`).
 */
export default function AcceptInvitationPage() {
  const params = useParams<{ token: string }>();
  const token = Array.isArray(params.token) ? params.token[0] : params.token;
  const router = useRouter();
  const queryClient = useQueryClient();
  const { setCurrentWorkspaceId } = useAppContext();
  const [status, setStatus] = useState<Status>({ kind: "accepting" });
  const started = useRef(false);

  useEffect(() => {
    // Accept exactly once, even under React StrictMode double-invoke.
    if (started.current || !token) return;
    started.current = true;

    (async () => {
      try {
        const response = await apiClient.post<AcceptResponse>(
          `/invitations/${encodeURIComponent(token)}/accept`,
          undefined
        );
        // The new workspace must appear in the flat membership list before we can switch into it.
        await queryClient.invalidateQueries({ queryKey: ["workspaces", "all"] });
        setStatus({ kind: "accepted", response });
      } catch (error) {
        const message =
          error instanceof ApiError
            ? error.status === 404
              ? "This invitation link is invalid or has already been used."
              : error.status === 409
                ? "This invitation is no longer valid — it may have expired or already been accepted."
                : error.message
            : "Something went wrong accepting this invitation.";
        setStatus({ kind: "error", message });
      }
    })();
  }, [token, queryClient]);

  function openWorkspace() {
    if (status.kind !== "accepted") return;
    setCurrentWorkspaceId(status.response.workspaceId);
    router.push("/app");
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12">
      <section className="w-full max-w-md rounded-[var(--radius)] border border-border bg-card p-8 text-center shadow-xl shadow-slate-950/10 dark:shadow-black/30">
        {status.kind === "accepting" ? (
          <>
            <h1 className="text-2xl font-semibold tracking-tight">Joining workspace…</h1>
            <p className="mt-3 text-sm leading-6 text-muted-foreground">
              Verifying your invitation link. This only takes a moment.
            </p>
          </>
        ) : null}

        {status.kind === "accepted" ? (
          <>
            <h1 className="text-2xl font-semibold tracking-tight">You&apos;re in!</h1>
            <p className="mt-3 text-sm leading-6 text-muted-foreground">
              You joined the workspace as {status.response.role}.
            </p>
            <button type="button" onClick={openWorkspace} className={buttonStyles({ className: "mt-6 w-full" })}>
              Open workspace
            </button>
          </>
        ) : null}

        {status.kind === "error" ? (
          <>
            <h1 className="text-2xl font-semibold tracking-tight">Invitation problem</h1>
            <p role="alert" className="mt-3 text-sm leading-6 text-red-600 dark:text-red-400">
              {status.message}
            </p>
            <Link href="/app" className={buttonStyles({ variant: "secondary", className: "mt-6 w-full" })}>
              Go to Planvexa
            </Link>
          </>
        ) : null}
      </section>
    </main>
  );
}
