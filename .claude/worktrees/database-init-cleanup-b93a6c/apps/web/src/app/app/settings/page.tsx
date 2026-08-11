import Link from "next/link";
import { settingsGroups } from "@/components/app-shell/nav-config";

export const metadata = { title: "Settings" };

export default function SettingsIndexPage() {
  return (
    <section aria-labelledby="settings-title" className="space-y-8">
      <div>
        <p className="text-sm font-medium text-primary">Workspace administration</p>
        <h1 id="settings-title" className="mt-2 text-3xl font-semibold tracking-tight">
          Settings
        </h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-muted-foreground">
          Everything that configures the workspace rather than the work in it.
        </p>
      </div>

      {settingsGroups.map((group) => (
        <div key={group.title} className="space-y-3">
          <div>
            <h2 className="text-lg font-semibold">{group.title}</h2>
            <p className="mt-1 text-sm text-muted-foreground">{group.description}</p>
          </div>
          <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {group.links.map((link) => (
              <li key={link.href}>
                <Link
                  href={link.href}
                  className="flex h-full flex-col rounded-[var(--radius)] border border-border bg-card p-4 shadow-sm transition-colors hover:bg-muted focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring motion-reduce:transition-none"
                >
                  <span className="text-sm font-semibold">{link.label}</span>
                  <span className="mt-1 text-sm leading-6 text-muted-foreground">
                    {link.description}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </section>
  );
}
