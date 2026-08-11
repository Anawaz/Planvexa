import Link from "next/link";
import { buttonStyles } from "@/components/ui/Button";

export default function NotFound() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12">
      <section className="max-w-md rounded-[var(--radius)] border border-border bg-card p-8 text-center shadow-xl shadow-slate-950/10 dark:shadow-black/30">
        <p className="text-sm font-medium text-primary">404</p>
        <h1 className="mt-2 text-3xl font-semibold tracking-tight">Page not found</h1>
        <p className="mt-3 text-sm leading-6 text-muted-foreground">
          The page you requested does not exist.
        </p>
        <Link className={buttonStyles({ className: "mt-6" })} href="/">
          Go home
        </Link>
      </section>
    </main>
  );
}
