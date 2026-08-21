"use client";

import type { ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { StatusBadge } from "@/components/admin/StatusBadge";
import { QueryState } from "@/components/ui/QueryState";
import { getInstanceHealth } from "@/lib/host/client";
import { hostKeys } from "@/lib/host/queries";
import { PageHeader, StatTile, panelClassName } from "./host-ui";

function Row({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-3 border-t border-border px-4 py-3 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  );
}

export function HealthPageClient() {
  const healthQuery = useQuery({
    queryKey: hostKeys.health(),
    queryFn: getInstanceHealth,
    // The point of this page is a current reading, not a cached one.
    refetchInterval: 30_000,
  });

  const health = healthQuery.data;

  return (
    <section aria-labelledby="host-health-title" className="space-y-6">
      <PageHeader
        id="host-health-title"
        eyebrow="Host administration"
        title="Instance health"
        description="A live reading of this server. Separate from the /health/live and /health/ready probes, which answer a load balancer's question rather than yours."
      />

      <QueryState query={healthQuery} loadingLabel="Reading instance health…">
        {health ? (
          <div className="space-y-6">
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <StatTile
                label="Database"
                value={health.databaseReachable ? "Reachable" : "Unreachable"}
                hint={health.databaseVersion ? `PostgreSQL ${health.databaseVersion}` : undefined}
              />
              <StatTile
                label="Outbox backlog"
                value={health.outboxPending}
                hint={health.outboxFailed > 0 ? `${health.outboxFailed} failed` : "No failures"}
              />
              <StatTile
                label="Errors (24h)"
                value={health.errorsLast24Hours}
                hint={`${health.warningsLast24Hours} warnings`}
              />
              <StatTile
                label="Schema version"
                value={health.appliedScripts}
                hint={health.latestScript ?? "Unknown"}
              />
            </div>

            {health.outboxFailed > 0 ? (
              <p
                role="alert"
                className="rounded-[var(--radius)] border border-red-300 bg-red-50 p-4 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
              >
                {health.outboxFailed} outbox message(s) failed to publish. Automations, webhooks and
                notifications depend on this queue draining — check the Logs page.
              </p>
            ) : null}

            {health.droppedLogRecords > 0 ? (
              <p
                role="alert"
                className="rounded-[var(--radius)] border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
              >
                {health.droppedLogRecords} log record(s) were dropped because the write queue was full.
                The Logs page is therefore incomplete — logging never blocks a request, so a burst is
                discarded rather than queued.
              </p>
            ) : null}

            {health.selfRegistrationEnabled && health.identityProviderRegistrationAllowed === false ? (
              <p
                role="alert"
                className="rounded-[var(--radius)] border border-red-300 bg-red-50 p-4 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
              >
                Self-registration is enabled here, but your identity provider is refusing to create new
                accounts — sign-up will fail with &ldquo;Registration not allowed&rdquo; until it is
                enabled there too. For Keycloak: Realm settings → Login → User registration.
              </p>
            ) : null}

            {health.selfRegistrationEnabled && !health.identityProviderManageable ? (
              <p className="rounded-[var(--radius)] border border-amber-300 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100">
                Self-registration is enabled here, but Planvexa cannot see or manage your identity
                provider, so it cannot confirm that sign-up actually works.
                {health.identityProviderDetail ? ` ${health.identityProviderDetail}` : null}
              </p>
            ) : null}

            {!health.maintenanceConnectionConfigured ? (
              <p className="rounded-[var(--radius)] border border-border bg-card p-4 text-sm text-muted-foreground">
                No maintenance connection is configured
                (<span className="font-mono">ConnectionStrings:PlanvexaMaintenance</span>). This console
                does not need one, but cross-workspace background sweeps — outbox drain, notification
                delivery, recurring tasks, retention — silently process nothing without it under a
                non-superuser database role.
              </p>
            ) : null}

            <div className={panelClassName}>
              <h2 className="p-4 text-sm font-semibold">Configuration</h2>
              <Row label="Environment" value={health.environment} />
              <Row label="Version" value={health.version ?? "Unknown"} />
              <Row label="File storage" value={health.fileStorageProvider} />
              <Row label="Email delivery" value={health.emailSender} />
              <Row
                label="Maintenance connection"
                value={
                  <StatusBadge
                    status={health.maintenanceConnectionConfigured ? "Configured" : "Not configured"}
                    tone={health.maintenanceConnectionConfigured ? "green" : "slate"}
                  />
                }
              />
              <Row
                label="Log capture"
                value={
                  <StatusBadge
                    status={health.logCaptureEnabled ? "Enabled" : "Disabled"}
                    tone={health.logCaptureEnabled ? "green" : "slate"}
                  />
                }
              />
              <Row label="Log minimum level" value={health.logMinimumLevel} />
              <Row label="Log retention" value={`${health.logRetentionDays} days`} />
              {/* Both halves of self-registration side by side — when sign-up is broken, it is almost
                  always because these two disagree. */}
              <Row
                label="Self-registration (Planvexa)"
                value={
                  <StatusBadge
                    status={health.selfRegistrationEnabled ? "Enabled" : "Disabled"}
                    tone={health.selfRegistrationEnabled ? "green" : "slate"}
                  />
                }
              />
              <Row
                label="Self-registration (identity provider)"
                value={
                  <StatusBadge
                    status={
                      health.identityProviderRegistrationAllowed === null
                        ? health.identityProviderManageable
                          ? "Unknown"
                          : "Not managed"
                        : health.identityProviderRegistrationAllowed
                          ? "Enabled"
                          : "Disabled"
                    }
                    tone={
                      health.identityProviderRegistrationAllowed === true
                        ? "green"
                        : health.identityProviderRegistrationAllowed === false
                          ? "red"
                          : "slate"
                    }
                  />
                }
              />
            </div>
          </div>
        ) : null}
      </QueryState>
    </section>
  );
}
