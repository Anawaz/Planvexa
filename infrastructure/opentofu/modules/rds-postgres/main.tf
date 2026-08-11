# Managed PostgreSQL for Planvexa. This is infrastructure-as-code provisioning a database instance
# for a self-hoster to use -- it does not conflict with AGENTS.md's "Planvexa never manages
# PostgreSQL" rule, which is about the application/dev-tooling never starting, stopping or
# configuring a Postgres *server process* itself. Standing up the server is exactly what a
# self-hoster's infrastructure layer is for.
#
# Assumes an existing VPC + subnets (var.vpc_id / var.subnet_ids) -- this module does not create
# networking. The master password is never held in Terraform state: manage_master_user_password
# delegates password generation/storage to AWS Secrets Manager, and the secret's ARN is this module's
# output -- read the actual password at deploy time (e.g. `aws secretsmanager get-secret-value`) when
# building the ConnectionStrings__Planvexa value for the Helm chart's API secret.

resource "aws_db_subnet_group" "this" {
  name       = "${var.identifier}-subnets"
  subnet_ids = var.subnet_ids
  tags       = var.tags
}

resource "aws_security_group" "this" {
  name        = "${var.identifier}-postgres"
  description = "Allow inbound Postgres (5432) from the app's network only."
  vpc_id      = var.vpc_id
  tags        = var.tags

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_security_group_rule" "ingress_security_groups" {
  for_each                 = toset(var.ingress_security_group_ids)
  type                     = "ingress"
  from_port                = 5432
  to_port                  = 5432
  protocol                 = "tcp"
  security_group_id        = aws_security_group.this.id
  source_security_group_id = each.value
}

resource "aws_security_group_rule" "ingress_cidrs" {
  count             = length(var.ingress_cidr_blocks) > 0 ? 1 : 0
  type              = "ingress"
  from_port         = 5432
  to_port           = 5432
  protocol          = "tcp"
  security_group_id = aws_security_group.this.id
  cidr_blocks       = var.ingress_cidr_blocks
}

resource "aws_db_instance" "this" {
  identifier     = var.identifier
  engine         = "postgres"
  engine_version = var.engine_version
  instance_class = var.instance_class

  allocated_storage = var.allocated_storage
  storage_type      = "gp3"
  storage_encrypted = true

  db_name                     = var.db_name
  username                    = var.master_username
  manage_master_user_password = true

  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = [aws_security_group.this.id]
  publicly_accessible    = var.publicly_accessible
  multi_az                = var.multi_az

  backup_retention_period   = var.backup_retention_period
  skip_final_snapshot       = var.skip_final_snapshot
  final_snapshot_identifier = var.skip_final_snapshot ? null : "${var.identifier}-final"

  deletion_protection = !var.skip_final_snapshot

  tags = var.tags
}
