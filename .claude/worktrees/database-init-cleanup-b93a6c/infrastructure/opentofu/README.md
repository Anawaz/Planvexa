# OpenTofu

Terraform-compatible modules for provisioning the cloud infrastructure a self-hoster needs around
Planvexa's Kubernetes deployment (`infrastructure/helm/planvexa`).

## Scope

"Which cloud" is a self-hoster's own choice, so this is necessarily example-oriented rather than an
exhaustive multi-cloud module set. **This directory targets AWS as the one reference implementation**,
using the standard `hashicorp/aws` provider (OpenTofu is a drop-in Terraform fork, so `tofu` and
`terraform` both work against these files). Porting the pattern to another cloud means swapping the
provider and the two resource types (object storage, managed Postgres) — the module boundaries and
variable shapes are deliberately provider-agnostic in naming.

**What's covered:**
- `modules/s3-storage` — an S3 bucket + scoped IAM user, for `FileStorage:S3` (the production
  equivalent of the dev MinIO container — see `docker-compose.yml`).
- `modules/rds-postgres` — a managed PostgreSQL instance, for `ConnectionStrings:Planvexa`.
- `examples/aws` — a root module wiring both together, with a `terraform.tfvars.example`.

**What's explicitly NOT covered** (self-hoster's own choice / out of scope here):
- Provisioning the Kubernetes cluster itself (EKS/GKE/AKS/k3s/...) — bring your own.
- Networking (VPC, subnets, NAT) — the modules here assume an existing VPC and subnet IDs.
- Keycloak — deploy it via its own official Helm chart or a hosted offering, same as
  `infrastructure/helm/planvexa`'s README documents for the app itself.

Provisioning Postgres here does not conflict with this repo's "Planvexa never manages PostgreSQL"
principle (`AGENTS.md`) — that rule is about the application and its dev tooling never starting,
stopping, or configuring a Postgres *server process*. Standing the server up via infrastructure code,
once, outside the app, is exactly what this directory is for.

## Layout

```
opentofu/
  modules/
    s3-storage/      main.tf  variables.tf  outputs.tf  versions.tf
    rds-postgres/     main.tf  variables.tf  outputs.tf  versions.tf
  examples/
    aws/              main.tf  variables.tf  outputs.tf  versions.tf  terraform.tfvars.example
```

## Usage

```bash
cd infrastructure/opentofu/examples/aws
cp terraform.tfvars.example terraform.tfvars   # fill in your VPC/subnet/security-group IDs
tofu init
tofu plan
tofu apply
```

Outputs give you the bucket name + access key, the Postgres endpoint, and the Secrets Manager ARN
holding the generated master password (the password itself is never written to state or outputs —
fetch it at deploy time and put it in the Kubernetes Secret the Helm chart expects, see
`infrastructure/helm/README.md`).

## Validation

Neither `tofu` nor `terraform` was available in the environment this was authored in (checked
`command -v tofu`/`terraform` — not found; not installed to work around this, per this environment's
policy against installing tools without asking first). The HCL was instead reviewed by hand for
`tofu validate`'s usual failure modes: every resource attribute matches the `hashicorp/aws` provider's
actual schema (v5.x), every module input/output referenced across files exists, `for_each`/`count`
usage is consistent with the variable's declared type, and no resource references an undeclared
variable. Run `tofu validate` (and `tofu fmt -check`) for real before applying.
