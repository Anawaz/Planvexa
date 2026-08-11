output "s3_bucket_name" {
  value = module.storage.bucket_name
}

output "s3_access_key_id" {
  value = module.storage.access_key_id
}

output "s3_secret_access_key" {
  value     = module.storage.secret_access_key
  sensitive = true
}

output "postgres_endpoint" {
  value = module.database.endpoint
}

output "postgres_master_user_secret_arn" {
  description = "Read the generated master password from this AWS Secrets Manager ARN -- it is never in Terraform state or these outputs."
  value       = module.database.master_user_secret_arn
}
