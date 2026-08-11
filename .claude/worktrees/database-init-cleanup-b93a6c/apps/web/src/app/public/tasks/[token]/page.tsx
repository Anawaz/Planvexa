import Link from "next/link";
import { notFound } from "next/navigation";
import { PublicCommentForm } from "@/components/collab/PublicCommentForm";
import { getPublicSharedTask } from "@/lib/collab/client";

type PublicTaskPageProps = {
  params: Promise<{
    token: string;
  }>;
};

// Anonymous: read straight from the API origin (the BFF proxy requires a session cookie).
export default async function PublicTaskPage({ params }: PublicTaskPageProps) {
  const { token } = await params;
  const task = await getPublicSharedTask(token);

  if (!task) {
    notFound();
  }

  return (
    <main className="min-h-screen bg-background px-6 py-10 sm:px-8">
      <section className="mx-auto max-w-3xl space-y-6 rounded-3xl border border-border bg-card p-6 shadow-xl">
        <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-5">
          <div>
            <p className="text-sm font-medium uppercase tracking-wide text-muted-foreground">
              Shared task
            </p>
            <h1 className="mt-2 text-3xl font-semibold tracking-tight">{task.title}</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              {task.allowsComments
                ? "Public link. Watchers and workspace-only fields are hidden; editing is never allowed."
                : "Read-only public link. Comments, watchers, and workspace-only fields are hidden."}
            </p>
          </div>
          <Link
            href="/login"
            className="rounded-lg border border-border bg-background px-4 py-2 text-sm font-medium hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            Open Planvexa
          </Link>
        </div>

        <div className="flex flex-wrap gap-2 text-sm">
          <span
            className={
              task.isCompleted
                ? "rounded-full bg-emerald-100 px-2.5 py-1 font-medium text-emerald-800 dark:bg-emerald-950 dark:text-emerald-200"
                : "rounded-full bg-muted px-2.5 py-1 text-muted-foreground"
            }
          >
            {task.isCompleted ? "Completed" : "In progress"}
          </span>
        </div>

        <section aria-labelledby="public-task-description" className="space-y-2">
          <h2 id="public-task-description" className="text-sm font-semibold">
            Description
          </h2>
          <p className="whitespace-pre-wrap text-sm leading-6 text-muted-foreground">
            {task.description || "No description provided."}
          </p>
        </section>

        {task.allowsComments ? <PublicCommentForm token={token} /> : null}

        <p className="border-t border-border pt-4 text-xs text-muted-foreground">
          This link can be revoked at any time by the workspace owner.
        </p>
      </section>
    </main>
  );
}
