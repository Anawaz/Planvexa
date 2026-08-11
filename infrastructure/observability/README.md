# Observability stack (optional)

Prometheus + Grafana + Loki/Promtail for self-hosters who want dashboards, log aggregation and
alerting on top of what the API already emits. **Not required for local development** — dev tracing
already works out of the box via Jaeger (`docker-compose.yml` / `apps/apphost/AppHost.cs`). This is
an addable layer.

## What's here and why

The API (`apps/api/Planvexa.Api/Program.cs`, `AddOpenTelemetry()`) exports traces and metrics over
OTLP — it has no Prometheus `/metrics` endpoint of its own, and beyond the framework's own ASP.NET
Core / HttpClient instrumentation, it does not emit any custom counters or histograms today. So:

| Component | Role |
| --- | --- |
| `otel-collector` | Receives the API's OTLP traces + metrics, exposes metrics on a Prometheus-scrapeable endpoint (`:8889`), forwards traces to Jaeger |
| `prometheus` | Scrapes `otel-collector`, `postgres-exporter`, and `blackbox-exporter`; evaluates `prometheus/rules/alerts.yml` |
| `postgres-exporter` | Standard `postgres_exporter` pointed at your PostgreSQL instance (Planvexa never runs Postgres itself — see `AGENTS.md`) |
| `blackbox-exporter` | HTTP reachability probes against the API/web health endpoints, backing the "service unreachable" alert |
| `grafana` | Pre-provisioned with Prometheus + Loki datasources and the two dashboards below |
| `loki` / `promtail` | Log aggregation; promtail ships every container's stdout/stderr on the Docker host |

## Dashboards

Provisioned automatically (`grafana/provisioning/dashboards`, files under `.../json/`) — no click-ops:

- **`api-red.json`** — API health/performance (RED method): request rate by route, 5xx error rate,
  p50/p90/p99 latency, in-flight requests.
- **`postgres-infra.json`** — Postgres/infrastructure: `pg_up`, active backends vs. `max_connections`,
  transaction rate, buffer cache hit ratio, database size, deadlocks.

**No business-metrics dashboard.** The task that scoped this change called for one "if custom metrics
already exist" — they don't (verified: `Program.cs` registers only `AddAspNetCoreInstrumentation()` /
`AddHttpClientInstrumentation()`, no custom `Meter`/`Counter`/`Histogram` anywhere in the codebase). Add
one when the product actually emits domain metrics worth graphing.

## Alerts

`prometheus/rules/alerts.yml` — four rules, deliberately not more:

1. `PlanvexaApiHighErrorRate` — 5xx ratio > 5% for 5m.
2. `PlanvexaApiHighLatency` — p99 request duration > 2s for 5m.
3. `PlanvexaServiceUnreachable` — blackbox HTTP probe against the API/web health endpoints failing for 2m.
4. `PlanvexaMetricsPipelineDown` — Prometheus can't scrape `otel-collector` for 2m (metrics blind spot).

Prometheus evaluates and surfaces firing alerts at `http://localhost:9090/alerts` and in Grafana's
alerting view. No Alertmanager is bundled — routing alerts to email/Slack/PagerDuty is
deployment-specific; wire your own Alertmanager or Grafana contact points.

## Usage

```bash
docker compose -f infrastructure/observability/docker-compose.yml up -d
```

| Service | URL |
| --- | --- |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3001 (default `admin` / `admin`, override with `GRAFANA_ADMIN_USER` / `GRAFANA_ADMIN_PASSWORD`) |
| Loki | http://localhost:3100 (queried through Grafana, not directly) |

Point the API's OTLP export at the collector instead of (or in addition to) Jaeger:

```powershell
$env:OpenTelemetry__OtlpEndpoint = 'http://localhost:4318'
```

Set `POSTGRES_EXPORTER_DSN` to a real, ideally read-only, Postgres login before starting this stack —
the default in `docker-compose.yml` assumes the local dev `planvexa`/`planvexa` role reachable via
`host.docker.internal`.

The `blackbox-http` Prometheus job and the Promtail log scrape both use placeholder assumptions
(container names `api`/`web`, or "every container on this Docker host") — see the comments in
`prometheus/prometheus.yml` and `promtail/promtail-config.yaml`. Adjust them to match how you actually
run the API/web containers (this compose file intentionally does not bundle them — see item 4, the
Helm chart, for a real production deployment shape).

## Validated

`docker compose -f infrastructure/observability/docker-compose.yml config` passes with this
environment's Docker Compose v5.3.1 (no PostgreSQL required to validate — it only parses/renders the
compose file).
