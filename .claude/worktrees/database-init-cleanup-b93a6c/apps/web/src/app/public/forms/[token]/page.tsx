import Link from "next/link";
import { notFound } from "next/navigation";
import { PublicFormPageClient } from "@/components/collab/PublicFormPageClient";
import { getPublicForm } from "@/lib/collab/client";

type PublicFormPageProps = {
  params: Promise<{
    token: string;
  }>;
};

// Anonymous: read straight from the API origin (the BFF proxy requires a session cookie).
export default async function PublicFormPage({ params }: PublicFormPageProps) {
  const { token } = await params;
  const form = await getPublicForm(token);

  if (!form) {
    notFound();
  }

  return (
    <main className="min-h-screen bg-background px-6 py-10 sm:px-8">
      <section className="mx-auto max-w-3xl space-y-6 rounded-3xl border border-border bg-card p-6 shadow-xl">
        <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-5">
          <div>
            {form.brandingLogoUrl ? (
              // eslint-disable-next-line @next/next/no-img-element -- arbitrary external branding URL, no remote-pattern allowlisting needed.
              <img src={form.brandingLogoUrl} alt="" className="mb-3 h-10 w-auto object-contain" />
            ) : null}
            <p className="text-sm font-medium uppercase tracking-wide text-muted-foreground">
              Public form
            </p>
            <h1 className="mt-2 text-3xl font-semibold tracking-tight">{form.title}</h1>
            {form.description ? (
              <p className="mt-2 text-sm leading-6 text-muted-foreground">{form.description}</p>
            ) : null}
          </div>
          <Link
            href="/login"
            className="rounded-lg border border-border bg-background px-4 py-2 text-sm font-medium hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            Open Planvexa
          </Link>
        </div>

        <PublicFormPageClient
          token={token}
          fields={form.fields}
          brandingColor={form.brandingColor}
          confirmationMessage={form.confirmationMessage}
          confirmationRedirectUrl={form.confirmationRedirectUrl}
        />

        <p className="border-t border-border pt-4 text-xs text-muted-foreground">
          Submissions are routed to the configured list in the workspace.
        </p>
      </section>
    </main>
  );
}
