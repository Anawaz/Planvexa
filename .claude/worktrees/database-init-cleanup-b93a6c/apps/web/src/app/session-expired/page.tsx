import Link from "next/link";

export default function SessionExpiredPage() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12">
      <section className="w-full max-w-md rounded-[var(--radius)] border border-border bg-card p-8 text-center shadow-xl shadow-slate-950/10 dark:shadow-black/30">
        <p className="text-sm font-medium text-primary">Session expired</p>
        <h1 className="mt-3 text-3xl font-semibold tracking-tight">Please sign in again</h1>
        <p className="mt-3 text-sm leading-6 text-muted-foreground">Your sign-in attempt expired or could not be verified.</p>
        <Link href="/auth/login" className="mt-6 inline-flex h-11 items-center justify-center rounded-lg bg-primary px-4 text-sm font-medium text-primary-foreground transition-[transform,opacity] active:scale-[0.97]">
          Return to login
        </Link>
      </section>
    </main>
  );
}
