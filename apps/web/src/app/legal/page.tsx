import Link from "next/link";

export default function LegalPage() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12">
      <section className="w-full max-w-2xl rounded-[var(--radius)] border border-border bg-card p-8 shadow-xl shadow-slate-950/10 dark:shadow-black/30">
        <Link href="/" className="text-sm font-medium text-muted-foreground hover:text-foreground">
          ← Back to home
        </Link>
        <div className="mt-8 space-y-3">
          <p className="text-sm font-medium text-primary">Legal</p>
          <h1 className="text-3xl font-semibold tracking-tight">Planvexa</h1>
          <p className="text-sm leading-6 text-muted-foreground">
            This is an official Planvexa distribution. Copyright © 2026 Planvexa contributors.
          </p>
        </div>
        <div className="mt-8 space-y-4 text-sm leading-6 text-muted-foreground">
          <p>
            Planvexa is licensed under the{" "}
            <span className="font-medium text-foreground">AGPL-3.0-only</span>. The AGPL requires
            that anyone who interacts with this software over a network be able to get its
            complete corresponding source code.
          </p>
          <p>
            <a
              href="https://www.gnu.org/licenses/agpl-3.0.en.html"
              target="_blank"
              rel="noreferrer"
              className="underline underline-offset-4 hover:text-foreground"
            >
              GNU Affero General Public License, Version 3 only
            </a>
          </p>
          <p>
            Source code:{" "}
            <a
              href="https://github.com/Anawaz/Planvexa"
              target="_blank"
              rel="noreferrer"
              className="underline underline-offset-4 hover:text-foreground"
            >
              github.com/Anawaz/Planvexa
            </a>
          </p>
        </div>
      </section>
    </main>
  );
}
