variable "project" {
  type = string
}

variable "vpc_id" {
  type = string
}

variable "public_subnet_ids" {
  type = list(string)
}

variable "alb_sg_id" {
  type = string
}

variable "api_sg_id" {
  type = string
}

variable "worker_sg_id" {
  type = string
}

variable "documents_bucket_name" {
  type = string
}

variable "documents_bucket_arn" {
  type = string
}

variable "db_password_secret_arn" {
  type = string
}

variable "database_url" {
  type      = string
  sensitive = true
}

variable "jwt_secret" {
  type      = string
  sensitive = true
}

variable "cors_origins" {
  type        = string
  description = "Comma-separated list of allowed CORS origins (e.g. https://app.example.com)"
}
