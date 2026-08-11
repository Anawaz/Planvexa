# Example root module: wires the s3-storage and rds-postgres modules together for a self-hoster
# targeting AWS. Provisions the two STATEFUL external dependencies the Helm chart
# (infrastructure/helm/planvexa) expects to be handed via values -- it does not provision the
# Kubernetes cluster itself (bring your own EKS/GKE/AKS/k3s) or Keycloak (deploy via its own Helm
# chart). See infrastructure/opentofu/README.md for the full scope statement.

module "storage" {
  source = "../../modules/s3-storage"

  bucket_name = var.bucket_name
  tags = {
    Project = "planvexa"
  }
}

module "database" {
  source = "../../modules/rds-postgres"

  identifier                 = var.db_identifier
  vpc_id                     = var.vpc_id
  subnet_ids                 = var.subnet_ids
  ingress_security_group_ids = var.ingress_security_group_ids
  tags = {
    Project = "planvexa"
  }
}
