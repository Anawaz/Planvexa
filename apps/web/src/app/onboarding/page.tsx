"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Button, buttonStyles } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { ApiError, apiClient } from "@/lib/api-client";
import { useAppContext } from "@/lib/app-context/AppContext";

type CreatedWorkspace = { id: string; name: string; slug: string; status: string; createdAtUtc: string };

function slugify(name: string) {
  return name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 63);
}

export default function OnboardingPage() {
  const queryClient = useQueryClient();
  const router = useRouter();
  const { setCurrentWorkspaceId } = useAppContext();
  // The workspace was created but navigating into it failed — a distinct failure the user must see.
  const [bootstrapError, setBootstrapError] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: (body: { name: string; slug: string }) =>
      // Direct Workspace onboarding (ADR 0015): no Organization step. The server provisions the
      // starter Space/List and makes the caller Owner in one transaction.
      apiClient.post<CreatedWorkspace>("/workspaces", body),
    onSuccess: async (workspace) => {
      try {
        // Refetch memberships FIRST so the new workspace is already in the list, then select it
        // through the same path the workspace switcher uses. Seeding localStorage and hard-navigating
        // instead does not work: AppContext re-persists whichever workspace it resolves while the
        // membership list is still in flight, which put a user who already had a workspace straight
        // back into the old one after creating a new one.
        await queryClient.invalidateQueries({ queryKey: ["workspaces", "me"] });
        setCurrentWorkspaceId(workspace.id);
        router.push("/app");
      } catch (error) {
        setBootstrapError(error instanceof Error ? error.message : "Unknown error.");
      }
    },
  });

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const name = String(form.get("workspaceName")).trim();
    create.mutate({ name, slug: slugify(name) });
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12">
      <section className="w-full max-w-2xl rounded-[var(--radius)] border border-border bg-card p-8 shadow-xl shadow-slate-950/10 dark:shadow-black/30">
        <Link
          href="/"
          className="text-sm font-medium text-muted-foreground hover:text-foreground"
        >
          ← Back to home
        </Link>
        <div className="mt-8">
          <p className="text-sm font-medium text-primary">Workspace setup</p>
          <h1 className="mt-2 text-3xl font-semibold tracking-tight">
            Create your Planvexa workspace
          </h1>
          <p className="mt-3 text-sm leading-6 text-muted-foreground">
            You become the owner of the new workspace and can invite your team once it exists.
          </p>
        </div>

        <form className="mt-8 grid gap-5" onSubmit={handleSubmit}>
          <Input
            id="workspaceName"
            name="workspaceName"
            label="Workspace name"
            placeholder="Product operations"
            autoComplete="off"
            required
          />
          {create.error ? (
            <p role="alert" className="text-sm text-red-600 dark:text-red-400">
              {create.error instanceof ApiError ? create.error.message : "Could not create the workspace."}
            </p>
          ) : null}
          {bootstrapError ? (
            <div
              role="alert"
              className="rounded-lg border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200"
            >
              <p className="font-semibold">Your workspace is ready, but its first list is not.</p>
              <p className="mt-2 leading-6">
                We could not create the starter space and list ({bootstrapError}). Open Spaces and
                create one there — everything else works once a list exists.
              </p>
              <Link
                href="/app/spaces"
                className={buttonStyles({ variant: "secondary", size: "sm", className: "mt-3" })}
              >
                Continue to Spaces
              </Link>
            </div>
          ) : null}
          <div className="flex flex-col-reverse gap-3 pt-2 sm:flex-row sm:justify-end">
            <Link
              className="inline-flex h-11 items-center justify-center rounded-lg px-4 text-sm font-medium text-muted-foreground hover:text-foreground"
              href="/login"
            >
              I already have access
            </Link>
            <Button type="submit" disabled={create.isPending}>
              {create.isPending ? "Creating…" : "Create workspace"}
            </Button>
          </div>
        </form>
      </section>
    </main>
  );
}
