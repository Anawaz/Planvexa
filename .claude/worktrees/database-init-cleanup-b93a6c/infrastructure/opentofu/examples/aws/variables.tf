variable "region" {
  type    = string
  default = "us-east-1"
}

variable "vpc_id" {
  description = "Existing VPC to deploy Postgres into (e.g. the VPC your EKS cluster runs in). This example does not create a VPC."
  type        = string
}

variable "subnet_ids" {
  description = "At least two private subnet IDs in that VPC, in different AZs."
  type        = list(string)
}

variable "ingress_security_group_ids" {
  description = "Security groups allowed to reach Postgres on 5432 -- typically your EKS node/cluster security group."
  type        = list(string)
  default     = []
}

variable "bucket_name" {
  description = "Globally-unique S3 bucket name for FileStorage__S3__BucketName."
  type        = string
}

variable "db_identifier" {
  type    = string
  default = "planvexa"
}
