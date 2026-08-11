variable "identifier" {
  description = "RDS instance identifier."
  type        = string
}

variable "engine_version" {
  description = "PostgreSQL major/minor version. Planvexa targets PostgreSQL 18 (see AGENTS.md); check RDS's supported versions before changing this."
  type        = string
  default     = "18"
}

variable "instance_class" {
  type    = string
  default = "db.t4g.micro" # smallest ARM burstable class -- fine for a self-hoster's first deployment; size up for real traffic
}

variable "allocated_storage" {
  description = "Storage in GiB. gp3 autoscales reads/writes independently of size, so this mainly bounds cost."
  type        = number
  default     = 20
}

variable "db_name" {
  type    = string
  default = "planvexa"
}

variable "master_username" {
  type    = string
  default = "planvexa"
}

variable "vpc_id" {
  description = "Existing VPC to deploy into. This module does not create networking -- bring your own VPC/subnets."
  type        = string
}

variable "subnet_ids" {
  description = "At least two subnet IDs (in different AZs) from the given VPC, for the DB subnet group."
  type        = list(string)
}

variable "ingress_security_group_ids" {
  description = "Security groups (e.g. your EKS node group's, or the Helm chart's cluster ingress) allowed to reach Postgres on 5432. Prefer this over ingress_cidr_blocks when possible."
  type        = list(string)
  default     = []
}

variable "ingress_cidr_blocks" {
  description = "CIDR blocks allowed to reach Postgres on 5432, in addition to ingress_security_group_ids. Leave empty if you're using security-group-based ingress only."
  type        = list(string)
  default     = []
}

variable "publicly_accessible" {
  description = "Almost always false. Only true for a throwaway/demo instance reachable outside the VPC."
  type        = bool
  default     = false
}

variable "multi_az" {
  type    = bool
  default = false
}

variable "backup_retention_period" {
  description = "Days of automated backups RDS keeps, independent of scripts/backup-db.ps1's manual dumps."
  type        = number
  default     = 7
}

variable "skip_final_snapshot" {
  description = "Set true only for throwaway environments -- false means `tofu destroy` takes a final snapshot first."
  type        = bool
  default     = false
}

variable "tags" {
  type    = map(string)
  default = {}
}
