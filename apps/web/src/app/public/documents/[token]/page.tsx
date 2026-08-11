import Link from "next/link";
import { notFound } from "next/navigation";
import { getPublicSharedDocument } from "@/lib/collab/client";

type PublicDocumentPageProps = {
  params: Promise<{
    token: string;
  }>;
};

// Anonymous: read straight from the API origin (the BFF proxy requires a session cookie).
export default async function PublicDocumentPage({ params }: PublicDocumentPageProps) {
  const { token } = await params;
  const document = await getPublicSharedDocument(token);

  if (!document) {
    notFound();
  }

  return (
    <main className="min-h-screen bg-background px-6 py-10 sm:px-8">
      <section className="mx-auto max-w-3xl space-y-6 rounded-3xl border border-border bg-card p-6 shadow-xl">
        <div className="flex flex-wrap items-start justify-between gap-4 border-b border-border pb-5">
          <div>
            <p className="text-sm font-medium uppercase tracking-wide text-muted-foreground">
              Shared document
            </p>
            <h1 className="mt-2 text-3xl font-semibold tracking-tight">{document.title}</h1>
            <p className="mt-2 text-sm text-muted-foreground">
              Read-only public link. Comments, versions, and workspace-only fields are hidden; editing is
              never allowed.
            </p>
          </div>
          <Link
            href="/login"
            className="rounded-lg border border-border bg-background px-4 py-2 text-sm font-medium hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            Open Planvexa
          </Link>
        </div>

        <section aria-labelledby="public-document-content" className="space-y-2">
          <h2 id="public-document-content" className="sr-only">
            Content
          </h2>
          <pre className="whitespace-pre-wrap break-words font-sans text-sm leading-6 text-foreground">
            {document.contentMarkdown || "This document is empty."}
          </pre>
        </section>

        <p className="border-t border-border pt-4 text-xs text-muted-foreground">
          This link can be revoked at any time by the workspace owner.
        </p>
      </section>
    </main>
  );
}
