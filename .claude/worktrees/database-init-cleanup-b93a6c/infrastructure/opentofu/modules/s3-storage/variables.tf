variable "bucket_name" {
  description = "Globally-unique S3 bucket name. Matches FileStorage__S3__BucketName in the API's config."
  type        = string
}

variable "enable_versioning" {
  description = "Keep prior object versions -- protects attachments from accidental overwrite/delete."
  type        = bool
  default     = true
}

variable "force_destroy" {
  description = "Allow `tofu destroy` to delete a non-empty bucket. Leave false outside throwaway environments."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Tags applied to every resource this module creates."
  type        = map(string)
  default     = {}
}
