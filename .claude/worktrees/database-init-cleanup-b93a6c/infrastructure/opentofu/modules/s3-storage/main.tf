# Object storage for Planvexa's FileStorage:S3 provider (see apps/apphost/AppHost.cs's minio wiring
# in dev -- this module provisions the production equivalent). One bucket, private by default, with
# an IAM user scoped to only that bucket so a leaked access key can't reach anything else in the
# account.
#
# Not EKS-specific: if you're running on EKS, prefer IRSA (an IAM role bound to the app's Kubernetes
# ServiceAccount) over the long-lived access key pair this module creates -- swap the aws_iam_user /
# aws_iam_access_key resources below for an aws_iam_role with the same policy document.

resource "aws_s3_bucket" "this" {
  bucket        = var.bucket_name
  force_destroy = var.force_destroy
  tags          = var.tags
}

resource "aws_s3_bucket_versioning" "this" {
  bucket = aws_s3_bucket.this.id
  versioning_configuration {
    status = var.enable_versioning ? "Enabled" : "Disabled"
  }
}

resource "aws_s3_bucket_public_access_block" "this" {
  bucket                  = aws_s3_bucket.this.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "this" {
  bucket = aws_s3_bucket.this.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_iam_user" "app" {
  name = "${var.bucket_name}-app"
  tags = var.tags
}

resource "aws_iam_access_key" "app" {
  user = aws_iam_user.app.name
}

data "aws_iam_policy_document" "app" {
  statement {
    sid       = "ListBucket"
    actions   = ["s3:ListBucket"]
    resources = [aws_s3_bucket.this.arn]
  }
  statement {
    sid       = "ReadWriteObjects"
    actions   = ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"]
    resources = ["${aws_s3_bucket.this.arn}/*"]
  }
}

resource "aws_iam_user_policy" "app" {
  name   = "${var.bucket_name}-app-access"
  user   = aws_iam_user.app.name
  policy = data.aws_iam_policy_document.app.json
}
