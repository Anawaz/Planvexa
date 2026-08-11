# Helm

`planvexa/` deploys the API and web app (Deployments, Services, Ingress) built from
`infrastructure/docker/api.Dockerfile` and `infrastructure/docker/web.Dockerfile` — this chart does
not build or rebuild those images, it deploys them.

**PostgreSQL and Keycloak are not bundled.** This repo's own principle is "never manage PostgreSQL"
(see `AGENTS.md`); Keycloak is a shared identity provider you likely operate independently of any one
app. Provision both separately — a managed Postgres (see `infrastructure/opentofu/modules/rds-postgres`
for an example) or your own instance, and Keycloak via its own official Helm chart or hosted
offering — then point this chart at them through `values.yaml` (`api.config.keycloakAuthority`, the
connection string in the API secret, etc.). Nothing here spins up either as a sub-chart.

## Layout

```
planvexa/
  Chart.yaml
  values.yaml            # every value documented inline
  templates/
    _helpers.tpl
    api-configmap.yaml    api-deployment.yaml    api-service.yaml
    web-configmap.yaml    web-deployment.yaml    web-service.yaml
    ingress.yaml           # single Ingress: /api, /hubs, /scalar, /openapi -> api; / -> web
    NOTES.txt
```

## Secrets

Connection strings and other credentials are **never inlined into `values.yaml`**. The chart expects
two pre-existing Secrets (create them with `kubectl create secret generic`, an external-secrets
operator, Sealed Secrets, or your cloud's secret manager — whatever your cluster already uses) and
references them by name:

| Value | Secret must contain |
| --- | --- |
| `api.existingSecretName` (default `planvexa-api-secrets`) | `ConnectionStrings__Planvexa`, optionally `ConnectionStrings__PlanvexaMaintenance`, `FileStorage__S3__AccessKey` / `FileStorage__S3__SecretKey` if using S3 file storage |
| `web.existingSecretName` (default `planvexa-web-secrets`) | `PLANVEXA_WEB_SESSION_SECRET` |

## Install

```bash
kubectl create secret generic planvexa-api-secrets \
  --from-literal=ConnectionStrings__Planvexa='Host=...;Port=5432;Database=planvexa;Username=...;Password=...'
kubectl create secret generic planvexa-web-secrets \
  --from-literal=PLANVEXA_WEB_SESSION_SECRET="$(openssl rand -base64 32)"

helm install planvexa infrastructure/helm/planvexa \
  --set api.image.repository=<your-registry>/planvexa-api \
  --set api.image.tag=<tag> \
  --set web.image.repository=<your-registry>/planvexa-web \
  --set web.image.tag=<tag> \
  --set api.config.keycloakAuthority=https://keycloak.example.com/realms/planvexa \
  --set web.config.keycloakUrl=https://keycloak.example.com \
  --set ingress.host=planvexa.example.com
```

Read `docs/runbooks/install.md` before a first production install (DbUp runs on API startup against
whatever database the secret points at) and `docs/runbooks/upgrade.md` before upgrading an existing
release (DbUp is forward-only).

## Validation

`helm` was not available in the environment this chart was authored in (`helm: command not found`,
also checked Chocolatey/Scoop bin directories — not installed via those either). It was not installed
to work around this, per this environment's policy against installing tools without asking first. The
templates were instead reviewed by hand for `helm lint`'s usual failure modes: required
`Chart.yaml` fields present, every template's Kubernetes `apiVersion`/`kind` valid, label selectors
matching between each Deployment and its Service, `{{ }}` blocks balanced, and `toYaml`/`nindent`
usage matching the values' actual shape. Run `helm lint infrastructure/helm/planvexa` and
`helm template infrastructure/helm/planvexa` for real before deploying.
