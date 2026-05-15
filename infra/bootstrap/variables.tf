variable "region" {
  description = "AWS region"
  type        = string
  default     = "us-east-2"
}

variable "state_bucket_name" {
  description = "Name of the S3 bucket for Terraform state (must be globally unique)"
  type        = string
}

variable "lock_table_name" {
  description = "Name of the DynamoDB table for Terraform state locking"
  type        = string
  default     = "healthmanager-terraform-locks"
}
