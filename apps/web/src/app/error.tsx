"use client";

import Link from "next/link";
import { Button, buttonStyles } from "@/components/ui/Button";

export default function ErrorPage({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12">
      <section className="max-w-lg rounded-[var(--radius)] border border-border bg-card p-8 text-center shadow-xl shadow-slate-950/10 dark:shadow-black/30">
        <p className="text-sm font-medium text-primary">Something went wrong</p>
        <h1 className="mt-2 text-3xl font-semibold tracking-tight">
          Planvexa hit an error
        </h1>
        <p className="mt-3 text-sm leading-6 text-muted-foreground">
          {error.message || "An unexpected client error occurred."}
        </p>
        <div className="mt-6 flex flex-col gap-3 sm:flex-row sm:justify-center">
          <Button type="button" onClick={reset}>
            Try again
          </Button>
          <Link className={buttonStyles({ variant: "outline" })} href="/">
            Go home
          </Link>
        </div>
      </section>
    </main>
  );
}
