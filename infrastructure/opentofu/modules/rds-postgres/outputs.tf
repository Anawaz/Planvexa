output "endpoint" {
  value = aws_db_instance.this.endpoint
}

output "address" {
  value = aws_db_instance.this.address
}

output "port" {
  value = aws_db_instance.this.port
}

output "db_name" {
  value = aws_db_instance.this.db_name
}

output "master_username" {
  value = aws_db_instance.this.username
}

output "master_user_secret_arn" {
  description = "AWS Secrets Manager ARN holding the generated master password. Fetch at deploy time, do not put the password in Terraform state or values files."
  value       = aws_db_instance.this.master_user_secret[0].secret_arn
}
